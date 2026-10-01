namespace NotificationService.Application.Models;

/// <summary>
/// Reference to an attachment uploaded to blob storage for a notification.
/// </summary>
public class NotificationAttachment
{
    public string BlobName { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string? ContentType { get; set; }
}

