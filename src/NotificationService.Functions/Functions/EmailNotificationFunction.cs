using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Service Bus-triggered function that sends queued email notifications and updates the audit record.
/// </summary>
public sealed class EmailNotificationFunction
{
    private readonly IEmailSender _emailSender;
    private readonly IBlobStorage _blobStorage;
    private readonly IAuditStore _auditStore;
    private readonly ILogger<EmailNotificationFunction> _logger;

    public EmailNotificationFunction(
        IEmailSender emailSender,
        IBlobStorage blobStorage,
        IAuditStore auditStore,
        ILogger<EmailNotificationFunction> logger)
    {
        _emailSender = emailSender;
        _blobStorage = blobStorage;
        _auditStore = auditStore;
        _logger = logger;
    }

    /// <summary>
    /// Processes one email notification from the "email" queue.
    /// </summary>
    /// <param name="message">The serialized <see cref="NotificationMessage"/>.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Function("EmailNotificationFunction")]
    public async Task Run(
    [ServiceBusTrigger(
        "email",
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
            "Processing email notification {NotificationId}",
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

        var attachments = new List<EmailAttachmentFile>();

        try
        {
            // ----------------------------
            // Open all attachments
            // ----------------------------

            foreach (var attachment in notification.Attachments)
            {
                var stream = await _blobStorage.OpenReadAsync(
                    attachment.BlobName,
                    cancellationToken);

                attachments.Add(
                    new EmailAttachmentFile
                    {
                        Stream = stream,
                        FileName = attachment.FileName,
                        ContentType = attachment.ContentType
                    });
            }

            var providerMessageId =
                await _emailSender.SendAsync(
                    notification.Recipients,
                    notification.Subject,
                    notification.Text,
                    attachments,
                    cancellationToken);

            await _auditStore.UpdateStatusAsync(
                notification.NotificationId,
                channel,
                "Sent",
                providerMessageId,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Email notification {NotificationId} sent.",
                notification.NotificationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Email notification {NotificationId} failed.",
                notification.NotificationId);

            await _auditStore.UpdateStatusAsync(
                notification.NotificationId,
                channel,
                "Failed",
                errorMessage: ex.Message,
                cancellationToken: cancellationToken);

            throw;
        }
        finally
        {
            foreach (var attachment in attachments)
            {
                await attachment.Stream.DisposeAsync();
            }
        }
    }
}