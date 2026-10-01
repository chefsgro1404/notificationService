using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Receives Azure Communication Services call events for voice notifications
/// (call answered → play audio, playback finished → hang up, not answered → retry).
/// </summary>
public sealed class VoiceCallEventsFunction
{
    private readonly IVoiceCallSender _voiceCallSender;
    private readonly ILogger<VoiceCallEventsFunction> _logger;

    public VoiceCallEventsFunction(
        IVoiceCallSender voiceCallSender,
        ILogger<VoiceCallEventsFunction> logger)
    {
        _voiceCallSender = voiceCallSender;
        _logger = logger;
    }

    /// <summary>
    /// Receives call events posted by Azure Communication Services and passes them to the voice sender.
    /// </summary>
    /// <param name="request">The callback request; its query string carries the call context.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>200 OK when handled, or 400 if the events could not be processed.</returns>
    // Anonymous: ACS cannot send a function key. Events only drive calls this app created.
    [Function("VoiceCallEvents")]
    public async Task<IActionResult> Run(
            [HttpTrigger(
                    AuthorizationLevel.Anonymous,
                    "post",
                    Route = "voice/call-events")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);

        var body = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            await _voiceCallSender.HandleCallEventsAsync(
                body,
                request.QueryString.Value,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not process voice call events for notification {NotificationId}.",
                request.Query["notificationId"].ToString());

            return new BadRequestResult();
        }

        return new OkResult();
    }
}
