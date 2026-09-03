namespace NotificationService.Application.Models;

public class NotificationAttachment
{
    public string BlobName { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string? ContentType { get; set; }
}

