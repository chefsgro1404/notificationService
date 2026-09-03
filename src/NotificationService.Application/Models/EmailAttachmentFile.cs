namespace NotificationService.Application.Models;

public sealed class EmailAttachmentFile
{
    public required Stream Stream { get; init; }

    public required string FileName { get; init; }

    public string? ContentType { get; init; }
}
