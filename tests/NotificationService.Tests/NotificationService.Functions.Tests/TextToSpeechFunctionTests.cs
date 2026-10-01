using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Functions.Functions;

namespace NotificationService.Functions.Tests.Functions;

public class TextToSpeechFunctionTests
{
    private readonly Mock<ITextToSpeechService> _service = new();
    private readonly Mock<ILogger<TextToSpeechFunction>> _logger = new();

    private TextToSpeechFunction CreateFunction() =>
        new(_service.Object, _logger.Object);

    private static HttpRequest CreateRequest(
        string body,
        string contentType = "application/json")
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }

    private static TextToSpeechResult Result() => new()
    {
        Id = 1,
        NotificationId = Guid.NewGuid(),
        BlobName = "T2A/a.wav",
        AudioUrl = new Uri("https://acct.blob.core.windows.net/notifications/tts/a.wav?sp=r&sig=x"),
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(60),
        ContentType = "audio/wav",
        SizeBytes = 100,
        CharacterCount = 5
    };

    private static (int? StatusCode, string? Error) ErrorOf(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        var error = objectResult.Value!.GetType().GetProperty("error")!.GetValue(objectResult.Value) as string;
        return (objectResult.StatusCode, error);
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenNotJson()
    {
        var result = await CreateFunction().Run(CreateRequest("text=hi", "text/plain"), CancellationToken.None);

        Assert.Equal(400, ErrorOf(result).StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenBodyIsMalformed()
    {
        var result = await CreateFunction().Run(CreateRequest("{not json"), CancellationToken.None);

        var (status, error) = ErrorOf(result);
        Assert.Equal(400, status);
        Assert.Contains("valid JSON", error);
    }

    [Theory]
    [InlineData("null", "Text is required.")]
    [InlineData("{}", "Text is required.")]
    [InlineData("{\"text\":\"   \"}", "Text is required.")]
    public async Task Run_ReturnsBadRequest_WhenTextIsMissing(string body, string expected)
    {
        var result = await CreateFunction().Run(CreateRequest(body), CancellationToken.None);

        Assert.Equal((400, expected), ErrorOf(result));
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenTextIsTooLong()
    {
        var body = $"{{\"text\":\"{new string('a', 151)}\"}}";

        var result = await CreateFunction().Run(CreateRequest(body), CancellationToken.None);

        var (status, error) = ErrorOf(result);
        Assert.Equal(400, status);
        Assert.Contains("150 characters", error);
    }

    [Fact]
    public async Task Run_ReturnsOkWithResult_WhenAudioIsGenerated()
    {
        var expected = Result();

        _service
            .Setup(x => x.GenerateAsync("Hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateFunction().Run(
            CreateRequest("{\"text\":\"Hello\"}", "application/json; charset=utf-8"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task Run_ReturnsBadGateway_WhenSpeechFails()
    {
        _service
            .Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SpeechSynthesisException("401"));

        var result = await CreateFunction().Run(CreateRequest("{\"text\":\"Hello\"}"), CancellationToken.None);

        Assert.Equal(502, ErrorOf(result).StatusCode);
    }

    [Fact]
    public async Task Run_ReturnsServerError_WhenStorageFails()
    {
        _service
            .Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));

        var result = await CreateFunction().Run(CreateRequest("{\"text\":\"Hello\"}"), CancellationToken.None);

        Assert.Equal((500, "Unable to generate audio."), ErrorOf(result));
    }

    [Fact]
    public async Task Run_PropagatesCancellation()
    {
        _service
            .Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateFunction().Run(CreateRequest("{\"text\":\"Hello\"}"), CancellationToken.None));
    }
}
