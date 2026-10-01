namespace NotificationService.Application.Models;

/// <summary>
/// An opened attachment stream passed to an <see cref="Interfaces.IEmailSender"/>.
/// </summary>
public sealed class EmailAttachmentFile
{
    public required Stream Stream { get; init; }

    public required string FileName { get; init; }

    public string? ContentType { get; init; }
}
