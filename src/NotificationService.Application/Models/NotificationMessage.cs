using NotificationService.Domain.Enums;

namespace NotificationService.Application.Models;

/// <summary>
/// Service Bus message describing a notification to be delivered by a channel processor.
/// </summary>
public sealed record NotificationMessage
{
    public Guid NotificationId { get; init; }

    public NotificationChannel Channel { get; init; }

    public required List<string> Recipients { get; init; }

    public string? Subject { get; init; }

    public required string Text { get; init; }

    public List<NotificationAttachment> Attachments { get; set; } = [];

    /// <summary>Audio category of a voice notification; its linked audio is played on the call.</summary>
    public int? CategoryId { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}