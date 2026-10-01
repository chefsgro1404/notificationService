using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Application.Validation;
using NotificationService.Domain.Enums;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Accepts a prerecorded voice notification and queues it for
/// <see cref="SendPrerecordedVoiceNotificationFunction"/> (Telegram channel / "telegram" queue).
/// Form fields: "recipient" (comma-separated E.164 numbers, may repeat), "categoryId" (audio category whose
/// linked active audio is played), optional "text".
/// </summary>
public sealed class VoiceNotificationIngressFunction
{
    private const NotificationChannel Channel = NotificationChannel.Telegram;

    private readonly IAuditStore _auditStore;
    private readonly INotificationPublisher _publisher;
    private readonly IAudioCategoryService _categories;
    private readonly ILogger<VoiceNotificationIngressFunction> _logger;

    /// <summary>
    /// Creates the function.
    /// </summary>
    /// <param name="auditStore">Records the accepted notification.</param>
    /// <param name="publisher">Queues the notification.</param>
    /// <param name="categories">Checks that the audio category has playable audio.</param>
    /// <param name="logger">Logger for failures.</param>
    public VoiceNotificationIngressFunction(
        IAuditStore auditStore,
        INotificationPublisher publisher,
        IAudioCategoryService categories,
        ILogger<VoiceNotificationIngressFunction> logger)
    {
        _auditStore = auditStore;
        _publisher = publisher;
        _categories = categories;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a voice notification request and queues it.
    /// </summary>
    /// <param name="request">The multipart/form-data HTTP request.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>202 Accepted with the notification id, 400 for invalid input, or 500 on failure.</returns>
    [Function("VoiceNotificationIngress")]
    public async Task<IActionResult> Run(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "post",
                    Route = "voice-notifications")]
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

        var recipients = VoiceRecipients.Split(
            form["recipient"]);

        if (recipients.Count == 0)
        {
            return new BadRequestObjectResult(
                new
                {
                    error = "At least one recipient is required."
                });
        }

        var invalidRecipients = recipients
            .Where(x => !VoiceRecipients.IsValid(x))
            .ToList();

        if (invalidRecipients.Count > 0)
        {
            return new BadRequestObjectResult(
                new
                {
                    error = "Recipients must be E.164 phone numbers (e.g. +18005551234).",
                    invalidRecipients
                });
        }

        if (!int.TryParse(
                form["categoryId"].ToString().Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var categoryId) ||
            categoryId < 1)
        {
            return new BadRequestObjectResult(
                new
                {
                    error = "categoryId is required and must be a positive whole number."
                });
        }

        try
        {
            // Fail fast: the category must be linked to active audio.
            await _categories.ResolveAudioBlobAsync(
                categoryId,
                cancellationToken);
        }
        catch (AudioCategoryException ex)
        {
            return new BadRequestObjectResult(
                new
                {
                    error = ex.Message
                });
        }

        var text =
            form["text"].ToString();

        var notificationId = Guid.NewGuid();

        try
        {
            // ----------------------------
            // 1. Create audit record
            // ----------------------------

            await _auditStore.CreateAcceptedAsync(
                notificationId,
                Channel.ToString(),
                string.Join(",", recipients),
                null,
                cancellationToken);

            // ----------------------------
            // 2. Create small message
            // ----------------------------

            var message = new NotificationMessage
            {
                NotificationId = notificationId,
                Channel = Channel,
                Recipients = recipients,
                CategoryId = categoryId,
                Text = string.IsNullOrWhiteSpace(text)
                    ? "Prerecorded voice notification"
                    : text,
                CreatedAtUtc = DateTime.UtcNow
            };

            // ----------------------------
            // 3. Publish
            // ----------------------------

            await _publisher.PublishAsync(
                message,
                cancellationToken);

            // ----------------------------
            // 4. Return immediately
            // ----------------------------

            return new AcceptedResult(
                $"/api/voice-notifications/{notificationId}",
                new
                {
                    notificationId,
                    status = "Accepted",
                    recipients = recipients.Count,
                    categoryId
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to accept voice notification {NotificationId}",
                notificationId);

            return new ObjectResult(
                new
                {
                    error = "Unable to accept voice notification."
                })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
