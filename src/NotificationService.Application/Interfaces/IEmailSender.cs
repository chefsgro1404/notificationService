using NotificationService.Application.Models;

namespace NotificationService.Application.Interfaces;

public interface IEmailSender
{
    Task<string?> SendAsync(
        IEnumerable<string> recipients,
        string? subject,
        string text,
        IEnumerable<EmailAttachmentFile>? attachments,
        CancellationToken cancellationToken);
}