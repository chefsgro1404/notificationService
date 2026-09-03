using NotificationService.Domain.Enums;

namespace NotificationService.Application.Models;

public sealed record NotificationMessage
{
    public Guid NotificationId { get; init; }

    public NotificationChannel Channel { get; init; }

    public required List<string> Recipients { get; init; }

    public string? Subject { get; init; }

    public required string Text { get; init; }

    public List<NotificationAttachment> Attachments { get; set; } = [];

    public DateTime CreatedAtUtc { get; init; }
}