using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Functions.Functions;

namespace NotificationService.Functions.Tests.Functions;

public class AudioCategoryFunctionTests
{
    private readonly Mock<IAudioCategoryService> _service = new();

    private AudioCategoryFunction CreateFunction() =>
        new(_service.Object, Mock.Of<ILogger<AudioCategoryFunction>>());

    private static AudioCategory Category(int id, string name, int? audioId = null) => new()
    {
        Id = id,
        Name = name,
        AudioId = audioId,
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        UpdatedAtUtc = DateTimeOffset.UnixEpoch
    };

    [Fact]
    public async Task List_ReturnsCategories()
    {
        IReadOnlyList<AudioCategory> categories = [Category(1, "Support")];
        _service.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(categories);

        var result = await CreateFunction().List(LogRequests.Get(), CancellationToken.None);

        Assert.Same(categories, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task List_ReturnsServerError_WhenServiceFails()
    {
        _service.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));

        var result = await CreateFunction().List(LogRequests.Get(), CancellationToken.None);

        Assert.Equal((500, "Unable to list audio categories."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task Create_ReturnsCreatedCategory()
    {
        var category = Category(3, "Support");
        _service.Setup(x => x.CreateAsync("Support", It.IsAny<CancellationToken>())).ReturnsAsync(category);

        var result = await CreateFunction().Create(LogRequests.Json("{\"name\":\"Support\"}"), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Same(category, created.Value);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_ForInvalidName()
    {
        _service.Setup(x => x.CreateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Category name must be 1 to 50 characters.", "name"));

        var result = await CreateFunction().Create(LogRequests.Json("{\"name\":\"\"}"), CancellationToken.None);

        Assert.Equal((400, "Category name must be 1 to 50 characters."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task Create_KeepsMessage_WhenArgumentExceptionHasNoParameterName()
    {
        _service.Setup(x => x.CreateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Bad name."));

        var result = await CreateFunction().Create(LogRequests.Json("{\"name\":\"x\"}"), CancellationToken.None);

        Assert.Equal((400, "Bad name."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task Create_ReturnsConflict_ForDuplicateName()
    {
        _service.Setup(x => x.CreateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AudioCategoryException("A category named \"Support\" already exists."));

        var result = await CreateFunction().Create(LogRequests.Json("{\"name\":\"Support\"}"), CancellationToken.None);

        Assert.Equal((409, "A category named \"Support\" already exists."), LogRequests.ErrorOf(result));
    }

    [Theory]
    [InlineData("{bad", "application/json")]
    [InlineData("null", "application/json")]
    [InlineData("name=x", "text/plain")]
    public async Task Create_ReturnsBadRequest_ForInvalidBody(string body, string contentType)
    {
        var result = await CreateFunction().Create(LogRequests.Json(body, contentType), CancellationToken.None);

        Assert.Equal(400, LogRequests.ErrorOf(result).Status);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAudio_ReturnsUpdatedCategory()
    {
        var category = Category(2, "Support", 7);
        _service.Setup(x => x.LinkAudioAsync(2, 7, It.IsAny<CancellationToken>())).ReturnsAsync(category);

        var result = await CreateFunction().LinkAudio(LogRequests.Json("{\"audioId\":7}"), 2, CancellationToken.None);

        Assert.Same(category, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task LinkAudio_Returns404_WhenCategoryIsMissing()
    {
        var result = await CreateFunction().LinkAudio(LogRequests.Json("{\"audioId\":7}"), 9, CancellationToken.None);

        Assert.Equal((404, "Category 9 was not found."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task LinkAudio_ReturnsConflict_WhenAudioIsNotActive()
    {
        _service.Setup(x => x.LinkAudioAsync(2, 7, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AudioCategoryException("Only active audio can be linked; audio 7 is Inactive."));

        var result = await CreateFunction().LinkAudio(LogRequests.Json("{\"audioId\":7}"), 2, CancellationToken.None);

        Assert.Equal(409, LogRequests.ErrorOf(result).Status);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"audioId\":0}")]
    [InlineData("{\"audioId\":-1}")]
    [InlineData("null")]
    public async Task LinkAudio_ReturnsBadRequest_ForInvalidBody(string body)
    {
        var result = await CreateFunction().LinkAudio(LogRequests.Json(body), 2, CancellationToken.None);

        Assert.Equal(400, LogRequests.ErrorOf(result).Status);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_PropagatesCancellation()
    {
        _service.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateFunction().List(LogRequests.Get(), CancellationToken.None));
    }
}
