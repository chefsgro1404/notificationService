using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;
using NotificationService.Functions.Functions;

namespace NotificationService.Functions.Tests.Functions;

public class VoiceNotificationIngressFunctionTests
{
    private readonly Mock<IAuditStore> _auditStore = new();
    private readonly Mock<INotificationPublisher> _publisher = new();
    private readonly Mock<IAudioCategoryService> _categories = new();
    private readonly Mock<ILogger<VoiceNotificationIngressFunction>> _logger = new();

    public VoiceNotificationIngressFunctionTests()
    {
        _categories
            .Setup(x => x.ResolveAudioBlobAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync("T2A/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav");
    }

    private VoiceNotificationIngressFunction CreateFunction()
    {
        return new VoiceNotificationIngressFunction(
            _auditStore.Object,
            _publisher.Object,
            _categories.Object,
            _logger.Object);
    }

    private static HttpRequest CreateRequest(
        Dictionary<string, string[]> fields,
        string contentType = "multipart/form-data; boundary=test")
    {
        var context = new DefaultHttpContext();

        context.Request.ContentType = contentType;

        if (!fields.ContainsKey("categoryId"))
        {
            fields["categoryId"] = ["5"];
        }

        context.Request.Form = new FormCollection(
            fields.ToDictionary(
                x => x.Key,
                x => new StringValues(x.Value)));

        return context.Request;
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenRequestIsNotMultipart()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";

        // Act
        var result = await CreateFunction().Run(
            context.Request,
            CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenNoRecipientsAreProvided()
    {
        // Act
        var result = await CreateFunction().Run(
            CreateRequest(new() { ["recipient"] = [" , "] }),
            CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);

        _publisher.Verify(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenAPhoneNumberIsInvalid()
    {
        // Act
        var result = await CreateFunction().Run(
            CreateRequest(new() { ["recipient"] = ["+18173235812, 12345"] }),
            CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);

        _auditStore.Verify(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_AuditsAndPublishesToTelegram_WhenRequestIsValid()
    {
        // Arrange
        NotificationMessage? published = null;

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .Callback<NotificationMessage, CancellationToken>((m, _) => published = m);

        // Act
        var result = await CreateFunction().Run(
            CreateRequest(new() { ["recipient"] = ["+18173235812, +16823005360", "+18173235812"] }),
            CancellationToken.None);

        // Assert
        Assert.IsType<AcceptedResult>(result);

        Assert.NotNull(published);
        Assert.Equal(NotificationChannel.Telegram, published!.Channel);
        Assert.Equal(["+18173235812", "+16823005360"], published.Recipients);
        Assert.Equal(5, published.CategoryId);
        Assert.False(string.IsNullOrWhiteSpace(published.Text));

        _auditStore.Verify(x => x.CreateAcceptedAsync(
                published.NotificationId,
                "Telegram",
                "+18173235812,+16823005360",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_UsesProvidedText_WhenTextIsGiven()
    {
        // Arrange
        NotificationMessage? published = null;

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .Callback<NotificationMessage, CancellationToken>((m, _) => published = m);

        // Act
        await CreateFunction().Run(
            CreateRequest(new() { ["recipient"] = ["+18173235812"], ["text"] = ["Custom text"] }),
            CancellationToken.None);

        // Assert
        Assert.Equal("Custom text", published!.Text);
    }

    [Fact]
    public async Task Run_ReturnsInternalServerError_WhenPublishFails()
    {
        // Arrange
        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));

        // Act
        var result = await CreateFunction().Run(
            CreateRequest(new() { ["recipient"] = ["+18173235812"] }),
            CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task Run_ReturnsBadRequest_WhenCategoryIdIsMissingOrInvalid(string categoryId)
    {
        var result = await CreateFunction().Run(
            CreateRequest(new() { ["recipient"] = ["+18173235812"], ["categoryId"] = [categoryId] }),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("categoryId", badRequest.Value!.ToString());
        _categories.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenCategoryHasNoPlayableAudio()
    {
        _categories
            .Setup(x => x.ResolveAudioBlobAsync(9, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AudioCategoryException("Category \"Sales\" has no linked audio."));

        var result = await CreateFunction().Run(
            CreateRequest(new() { ["recipient"] = ["+18173235812"], ["categoryId"] = ["9"] }),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("has no linked audio", badRequest.Value!.ToString());
        _publisher.Verify(x => x.PublishAsync(It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
