using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Application.Validation;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Places a voice call to every recipient of a notification queued by
/// <see cref="VoiceNotificationIngressFunction"/> (Telegram channel / "telegram" queue), playing the active
/// audio linked to the notification's audio category.
/// </summary>
public sealed class SendPrerecordedVoiceNotificationFunction
{
    private readonly IVoiceCallSender _voiceCallSender;
    private readonly IAuditStore _auditStore;
    private readonly IAudioCategoryService _categories;
    private readonly ILogger<SendPrerecordedVoiceNotificationFunction> _logger;

    /// <summary>
    /// Creates the function.
    /// </summary>
    /// <param name="voiceCallSender">Places the calls.</param>
    /// <param name="auditStore">Tracks the notification status.</param>
    /// <param name="categories">Resolves the audio category to its audio file.</param>
    /// <param name="logger">Logger for progress and failures.</param>
    public SendPrerecordedVoiceNotificationFunction(
        IVoiceCallSender voiceCallSender,
        IAuditStore auditStore,
        IAudioCategoryService categories,
        ILogger<SendPrerecordedVoiceNotificationFunction> logger)
    {
        _voiceCallSender = voiceCallSender;
        _auditStore = auditStore;
        _categories = categories;
        _logger = logger;
    }

    /// <summary>
    /// Processes one voice notification from the "telegram" queue and starts a call to each recipient.
    /// </summary>
    /// <param name="message">The serialized <see cref="NotificationMessage"/>.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Function("SendPrerecordedVoiceNotification")]
    public async Task Run(
    [ServiceBusTrigger(
        "telegram",
        Connection = "ServiceBusConnection")]
    string message,
    CancellationToken cancellationToken)
    {
        var notification =
            JsonSerializer.Deserialize<NotificationMessage>(
                message);

        if (notification is null)
        {
            throw new InvalidOperationException(
                "Invalid notification message.");
        }

        _logger.LogInformation(
            "Processing voice notification {NotificationId}",
            notification.NotificationId);

        var channel = notification.Channel.ToString();

        // Idempotency check
        if (await _auditStore.HasBeenProcessedAsync(
                notification.NotificationId,
                channel,
                cancellationToken))
        {
            _logger.LogInformation(
                "Notification {NotificationId} was already sent.",
                notification.NotificationId);

            return;
        }

        await _auditStore.UpdateStatusAsync(
            notification.NotificationId,
            channel,
            "Processing",
            cancellationToken: cancellationToken);

        try
        {
            var recipients = VoiceRecipients.Split(notification.Recipients);

            var failures = recipients
                .Where(x => !VoiceRecipients.IsValid(x))
                .Select(x => $"{x}: invalid phone number")
                .ToList();

            var validRecipients = recipients
                .Where(VoiceRecipients.IsValid)
                .ToList();

            if (validRecipients.Count == 0)
            {
                // Nothing to retry: the message itself is invalid.
                _logger.LogError(
                    "Voice notification {NotificationId} has no valid phone numbers.",
                    notification.NotificationId);

                await _auditStore.UpdateStatusAsync(
                    notification.NotificationId,
                    channel,
                    "Failed",
                    errorCode: "InvalidRecipients",
                    errorMessage: string.Join("; ", failures),
                    cancellationToken: cancellationToken);

                return;
            }

            // ----------------------------
            // Resolve the category's audio
            // ----------------------------

            string audioBlobName;

            try
            {
                audioBlobName = notification.CategoryId is { } categoryId
                    ? await _categories.ResolveAudioBlobAsync(categoryId, cancellationToken)
                    : throw new AudioCategoryException("The notification has no audio category.");
            }
            catch (AudioCategoryException ex)
            {
                // Nothing to retry until someone links active audio to the category.
                _logger.LogError(
                    "Voice notification {NotificationId} has no playable audio: {Reason}",
                    notification.NotificationId,
                    ex.Message);

                await _auditStore.UpdateStatusAsync(
                    notification.NotificationId,
                    channel,
                    "Failed",
                    errorCode: "AudioUnavailable",
                    errorMessage: ex.Message,
                    cancellationToken: cancellationToken);

                return;
            }

            // ----------------------------
            // Call each recipient
            // ----------------------------

            var callIds = new List<string>();

            foreach (var recipient in validRecipients)
            {
                try
                {
                    var callId = await _voiceCallSender.StartCallAsync(
                        recipient,
                        notification.NotificationId,
                        audioBlobName,
                        cancellationToken);

                    callIds.Add(callId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Voice call for notification {NotificationId} to recipient #{RecipientIndex} failed.",
                        notification.NotificationId,
                        validRecipients.IndexOf(recipient) + 1);

                    failures.Add($"{recipient}: {ex.Message}");
                }
            }

            if (callIds.Count == 0)
            {
                // Every call failed; let Service Bus retry.
                throw new InvalidOperationException(
                    "All voice calls failed. " + string.Join("; ", failures));
            }

            // Some calls started: mark Sent so a redelivery does not call them again.
            // Call ids go to the log only; they are not stored in the audit table.
            await _auditStore.UpdateStatusAsync(
                notification.NotificationId,
                channel,
                "Sent",
                errorCode: failures.Count > 0 ? "PartialFailure" : null,
                errorMessage: failures.Count > 0 ? string.Join("; ", failures) : null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Voice notification {NotificationId} started {CallCount} call(s) {CallIds}, {FailureCount} failure(s).",
                notification.NotificationId,
                callIds.Count,
                string.Join(",", callIds),
                failures.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Voice notification {NotificationId} failed.",
                notification.NotificationId);

            await _auditStore.UpdateStatusAsync(
                notification.NotificationId,
                channel,
                "Failed",
                errorMessage: ex.Message,
                cancellationToken: cancellationToken);

            throw;
        }
    }
}
