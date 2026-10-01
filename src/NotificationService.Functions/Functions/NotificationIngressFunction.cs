using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;

namespace NotificationService.Functions.Functions;

/// <summary>
/// HTTP-triggered function that accepts a notification (multipart/form-data), stores attachments,
/// creates the audit record and queues it for the channel processor.
/// </summary>
public sealed class NotificationIngressFunction
{
    private readonly IBlobStorage _blobStorage;
    private readonly IAuditStore _auditStore;
    private readonly INotificationPublisher _publisher;
    private readonly ILogger<NotificationIngressFunction> _logger;

    public NotificationIngressFunction(
        IBlobStorage blobStorage,
        IAuditStore auditStore,
        INotificationPublisher publisher,
        ILogger<NotificationIngressFunction> logger)
    {
        _blobStorage = blobStorage;
        _auditStore = auditStore;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a notification request and queues it.
    /// </summary>
    /// <param name="request">The multipart/form-data HTTP request.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>202 Accepted with the notification id, 400 for invalid input, or 500 on failure.</returns>
    [Function("NotificationIngress")]
    public async Task<IActionResult> Run(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "post",
                    Route = "notifications")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return new BadRequestObjectResult(
                new
                {
                    error = "Content-Type must be multipart/form-data."
                });
        }

        var form = await request.ReadFormAsync(
            cancellationToken);

        if (!Enum.TryParse<NotificationChannel>(
                form["channel"],
                true,
                out var channel))
        {
            return new BadRequestObjectResult(
                new
                {
                    error = "Invalid channel."
                });
        }

        var recipients = form["recipient"]
            .Select(x => x!.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var text =
            form["text"].ToString();

        var subject =
            form["subject"].ToString();

        if (recipients.Count == 0)
        {
            return new BadRequestObjectResult(
                new
                {
                    error = "At least one recipient is required."
                });
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new BadRequestObjectResult(
                new
                {
                    error = "Text is required."
                });
        }

        var attachments = new List<NotificationAttachment>();


        var notificationId = Guid.NewGuid();

        //string? blobName = null;

        try
        {
            // ----------------------------
            // 1. Upload attachment
            // ----------------------------

            foreach (var file in form.Files)
            {
                if (file is null || file.Length <= 0)
                    continue;

                await using var stream = file.OpenReadStream();

                var blobName = await _blobStorage.UploadAsync(
                    stream,
                    file.FileName,
                    file.ContentType,
                    notificationId,
                    cancellationToken);

                attachments.Add(
                    new NotificationAttachment
                    {
                        BlobName = blobName,
                        FileName = file.FileName,
                        ContentType = file.ContentType
                    });
            }

            // ----------------------------
            // 2. Create audit record
            // ----------------------------

            await _auditStore.CreateAcceptedAsync(
                notificationId,
                channel.ToString(),
                string.Join(",", recipients),
                string.Join(",", attachments.Select(x => x.BlobName)),
                cancellationToken);

            // ----------------------------
            // 3. Create small message
            // ----------------------------

            var message = new NotificationMessage
            {
                NotificationId = notificationId,
                Channel = channel,
                Recipients = recipients,
                Subject = string.IsNullOrWhiteSpace(subject)
                ? null
                : subject,
                Text = text,
                Attachments = attachments,
                CreatedAtUtc = DateTime.UtcNow
            };

            // ----------------------------
            // 4. Publish
            // ----------------------------

            await _publisher.PublishAsync(
                message,
                cancellationToken);

            // ----------------------------
            // 5. Return immediately
            // ----------------------------

            return new AcceptedResult(
                $"/api/notifications/{notificationId}",
                new
                {
                    notificationId,
                    status = "Accepted"
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to accept notification {NotificationId}",
                notificationId);

            // Cleanup all blobs that were successfully uploaded.
            foreach (var attachment in attachments)
            {
                try
                {
                    await _blobStorage.DeleteAsync(
                        attachment.BlobName,
                        cancellationToken);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(
                        cleanupException,
                        "Failed to clean up blob {BlobName}",
                        attachment.BlobName);
                }
            }

            return new ObjectResult(
                new
                {
                    error = "Unable to accept notification."
                })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}