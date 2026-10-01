using System.Web;
using Azure;
using Azure.Communication;
using Azure.Communication.CallAutomation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Services;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Voice;

/// <summary>
/// Places outbound PSTN calls with ACS Call Automation and plays the notification's audio blob
/// (resolved from its audio category) when the recipient answers (CallConnected), then hangs up.
/// An unanswered call (CreateCallFailed) is retried until Voice:MaxCallAttempts is reached (0 = never retried).
/// Recipients always come from the notification request; only the caller id comes from configuration.
/// The callback URL carries notificationId, recipient, attempt and audio so playback and retries need no state.
/// </summary>
public sealed class AzureCommunicationVoiceCallSender : IVoiceCallSender
{
    public const string CallbackRoute = "api/voice/call-events";

    private readonly CallAutomationClient _client;
    private readonly PhoneNumberIdentifier _caller;
    private readonly Uri _callbackBaseUri;
    private readonly VoiceOptions _voiceOptions;
    private readonly IBlobStorage _blobStorage;
    private readonly ILogger<AzureCommunicationVoiceCallSender> _logger;

    public AzureCommunicationVoiceCallSender(
        IOptions<AzureCommunicationServicesOptions> acsOptions,
        IOptions<VoiceOptions> voiceOptions,
        IBlobStorage blobStorage,
        ILogger<AzureCommunicationVoiceCallSender> logger)
        : this(
            CreateClient(acsOptions.Value),
            acsOptions,
            voiceOptions,
            blobStorage,
            logger)
    {
    }

    internal AzureCommunicationVoiceCallSender(
        CallAutomationClient client,
        IOptions<AzureCommunicationServicesOptions> acsOptions,
        IOptions<VoiceOptions> voiceOptions,
        IBlobStorage blobStorage,
        ILogger<AzureCommunicationVoiceCallSender> logger)
    {
        var acs = acsOptions.Value;
        _voiceOptions = voiceOptions.Value;

        if (string.IsNullOrWhiteSpace(acs.CallerPhoneNumber))
        {
            throw new InvalidOperationException(
                "AzureCommunicationServices:CallerPhoneNumber is required.");
        }

        if (!Uri.TryCreate(
                acs.CallbackBaseUrl.TrimEnd('/') + "/",
                UriKind.Absolute,
                out var callbackBaseUri)
            || callbackBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "AzureCommunicationServices:CallbackBaseUrl must be the public https:// URL of this Function App.");
        }

        _client = client;
        _caller = new PhoneNumberIdentifier(acs.CallerPhoneNumber.Trim());
        _callbackBaseUri = callbackBaseUri;
        _blobStorage = blobStorage;
        _logger = logger;
    }

    private static CallAutomationClient CreateClient(
        AzureCommunicationServicesOptions acs)
    {
        if (string.IsNullOrWhiteSpace(acs.ConnectionString))
        {
            throw new InvalidOperationException(
                "AzureCommunicationServices:ConnectionString is required.");
        }

        return new CallAutomationClient(acs.ConnectionString);
    }

    /// <inheritdoc />
    public Task<string> StartCallAsync(
        string recipient,
        Guid notificationId,
        string audioBlobName,
        CancellationToken cancellationToken)
    {
        if (!TextToSpeechService.IsAudioBlobName(audioBlobName))
        {
            throw new ArgumentException(
                $"'{audioBlobName}' is not a text-to-audio file.",
                nameof(audioBlobName));
        }

        return StartCallAttemptAsync(recipient, notificationId, audioBlobName, attempt: 1, cancellationToken);
    }

    private async Task<string> StartCallAttemptAsync(
        string recipient,
        Guid notificationId,
        string audioBlobName,
        int attempt,
        CancellationToken cancellationToken)
    {
        var callbackUri = new Uri(
            _callbackBaseUri,
            $"{CallbackRoute}?notificationId={notificationId}" +
            $"&recipient={Uri.EscapeDataString(recipient)}" +
            $"&attempt={attempt}" +
            $"&audio={Uri.EscapeDataString(audioBlobName)}");

        var invite = new CallInvite(
            new PhoneNumberIdentifier(recipient),
            _caller);

        var options = new CreateCallOptions(invite, callbackUri)
        {
            OperationContext = notificationId.ToString()
        };

        CreateCallResult result =
            await _client.CreateCallAsync(options, cancellationToken);

        return result.CallConnectionProperties.CallConnectionId;
    }

    /// <inheritdoc />
    public async Task HandleCallEventsAsync(
        string eventsJson,
        string? callbackQuery,
        CancellationToken cancellationToken)
    {
        var context = HttpUtility.ParseQueryString(callbackQuery ?? string.Empty);

        var events = CallAutomationEventParser.ParseMany(
            BinaryData.FromString(eventsJson));

        foreach (var acsEvent in events)
        {
            _logger.LogInformation(
                "Voice call event {EventType} for call {CallConnectionId}, notification {NotificationId}.",
                acsEvent.GetType().Name,
                acsEvent.CallConnectionId,
                acsEvent.OperationContext);

            // One failing event must not stop the others; ACS does not need a retry.
            try
            {
                switch (acsEvent)
                {
                    case CallConnected:
                        await PlayAudioAsync(acsEvent, context["audio"], cancellationToken);
                        break;

                    case PlayCompleted:
                        await HangUpAsync(acsEvent.CallConnectionId, cancellationToken);
                        break;

                    case PlayFailed playFailed:
                        _logger.LogError(
                            "Voice playback failed for call {CallConnectionId}. ReasonCode {ReasonCode}, Code {Code}, SubCode {SubCode}, Message {Message}.",
                            acsEvent.CallConnectionId,
                            playFailed.ReasonCode,
                            playFailed.ResultInformation?.Code,
                            playFailed.ResultInformation?.SubCode,
                            playFailed.ResultInformation?.Message);

                        await HangUpAsync(acsEvent.CallConnectionId, cancellationToken);
                        break;

                    case CreateCallFailed:
                        _logger.LogWarning(
                            "Voice call {CallConnectionId} was not answered or could not be established. Code {Code}, SubCode {SubCode}, Message {Message}.",
                            acsEvent.CallConnectionId,
                            acsEvent.ResultInformation?.Code,
                            acsEvent.ResultInformation?.SubCode,
                            acsEvent.ResultInformation?.Message);

                        await RetryCallAsync(context, cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to handle voice call event {EventType} for call {CallConnectionId}.",
                    acsEvent.GetType().Name,
                    acsEvent.CallConnectionId);
            }
        }
    }

    private async Task RetryCallAsync(
        System.Collections.Specialized.NameValueCollection context,
        CancellationToken cancellationToken)
    {
        var recipient = context["recipient"];
        var audioBlobName = context["audio"];

        if (!Guid.TryParse(context["notificationId"], out var notificationId)
            || string.IsNullOrWhiteSpace(recipient)
            || !int.TryParse(context["attempt"], out var attempt)
            || !TextToSpeechService.IsAudioBlobName(audioBlobName))
        {
            _logger.LogError("Cannot retry voice call: callback URL is missing the call context.");
            return;
        }

        if (attempt >= _voiceOptions.MaxCallAttempts)
        {
            _logger.LogError(
                "Voice call for notification {NotificationId} was not answered after {Attempts} attempt(s). Giving up.",
                notificationId,
                attempt);
            return;
        }

        var callId = await StartCallAttemptAsync(
            recipient,
            notificationId,
            audioBlobName!,
            attempt + 1,
            cancellationToken);

        _logger.LogInformation(
            "Retrying voice call for notification {NotificationId}: attempt {Attempt} of {MaxAttempts}, call {CallConnectionId}.",
            notificationId,
            attempt + 1,
            _voiceOptions.MaxCallAttempts,
            callId);
    }

    private async Task PlayAudioAsync(
        CallAutomationEventBase acsEvent,
        string? audioBlobName,
        CancellationToken cancellationToken)
    {
        if (!TextToSpeechService.IsAudioBlobName(audioBlobName))
        {
            _logger.LogError(
                "Call {CallConnectionId} has no valid audio in its callback URL; hanging up.",
                acsEvent.CallConnectionId);

            await HangUpAsync(acsEvent.CallConnectionId, cancellationToken);
            return;
        }

        var audioUri = _blobStorage.GetReadUri(
            audioBlobName!,
            TimeSpan.FromMinutes(_voiceOptions.AudioUrlValidityMinutes));

        var playOptions = new PlayToAllOptions(new FileSource(audioUri))
        {
            OperationContext = acsEvent.OperationContext
        };

        await _client
            .GetCallConnection(acsEvent.CallConnectionId)
            .GetCallMedia()
            .PlayToAllAsync(playOptions, cancellationToken);
    }

    private async Task HangUpAsync(
        string callConnectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client
                .GetCallConnection(callConnectionId)
                .HangUpAsync(forEveryone: true, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Recipient already hung up.
        }
    }
}
