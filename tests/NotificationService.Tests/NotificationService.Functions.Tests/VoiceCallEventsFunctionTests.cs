using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NotificationService.Application.Interfaces;
using NotificationService.Functions.Functions;

namespace NotificationService.Functions.Tests.Functions;

public class VoiceCallEventsFunctionTests
{
    private readonly Mock<IVoiceCallSender> _voiceCallSender = new();

    private VoiceCallEventsFunction CreateFunction() =>
        new(_voiceCallSender.Object, Mock.Of<ILogger<VoiceCallEventsFunction>>());

    private static HttpRequest CreateRequest(string body, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.QueryString = new QueryString(query);
        return context.Request;
    }

    [Fact]
    public async Task Run_PassesBodyAndQueryToSender_AndReturnsOk()
    {
        var result = await CreateFunction().Run(
            CreateRequest("[{\"type\":\"x\"}]", "?notificationId=abc&attempt=1"),
            CancellationToken.None);

        Assert.IsType<OkResult>(result);
        _voiceCallSender.Verify(x => x.HandleCallEventsAsync(
                "[{\"type\":\"x\"}]",
                "?notificationId=abc&attempt=1",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenEventsCannotBeProcessed()
    {
        _voiceCallSender
            .Setup(x => x.HandleCallEventsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("bad json"));

        var result = await CreateFunction().Run(
            CreateRequest("not json", "?notificationId=abc"),
            CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
    }
}
