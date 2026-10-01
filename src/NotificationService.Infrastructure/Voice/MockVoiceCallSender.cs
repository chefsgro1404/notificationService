using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Voice;

/// <summary>
/// Local development voice sender: no call is placed, the call is only logged.
/// </summary>
public sealed class MockVoiceCallSender : IVoiceCallSender
{
    private readonly VoiceOptions _options;
    private readonly IBlobStorage _blobStorage;
    private readonly ILogger<MockVoiceCallSender> _logger;

    public MockVoiceCallSender(
        IOptions<VoiceOptions> options,
        IBlobStorage blobStorage,
        ILogger<MockVoiceCallSender> logger)
    {
        _options = options.Value;
        _blobStorage = blobStorage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> StartCallAsync(
        string recipient,
        Guid notificationId,
        string audioBlobName,
        CancellationToken cancellationToken)
    {
        // Read the audio blob like ACS would, so a missing file fails locally too.
        long audioBytes;

        await using (var audio = await _blobStorage.OpenReadAsync(
                         audioBlobName,
                         cancellationToken))
        {
            audioBytes = audio.Length;
        }

        // Exercise the same audio URL generation as the real sender.
        var audioUri = _blobStorage.GetReadUri(
            audioBlobName,
            TimeSpan.FromMinutes(_options.AudioUrlValidityMinutes));

        var callId = $"mock-call-{Guid.NewGuid()}";

        _logger.LogInformation(
            "[MOCK] Voice call {CallId} to {Recipient} for notification {NotificationId} would play {AudioUrl} ({AudioBytes} bytes).",
            callId,
            recipient,
            notificationId,
            audioUri.GetLeftPart(UriPartial.Path),
            audioBytes);

        return callId;
    }

    /// <inheritdoc />
    public Task HandleCallEventsAsync(
        string eventsJson,
        string? callbackQuery,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[MOCK] Ignoring voice call events ({Length} bytes).",
            eventsJson.Length);

        return Task.CompletedTask;
    }
}
