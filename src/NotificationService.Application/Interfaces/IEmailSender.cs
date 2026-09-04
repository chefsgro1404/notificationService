using NotificationService.Application.Models;

namespace NotificationService.Application.Interfaces;

/// <summary>
/// Defines operations for sending email notifications.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email notification to one or more recipients.
    /// </summary>
    /// <param name="recipients">The email addresses of the recipients.</param>
    /// <param name="subject">The optional subject of the email.</param>
    /// <param name="text">The plain-text body of the email.</param>
    /// <param name="attachments">The optional files to attach to the email.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// A task containing the provider message ID if the email was sent successfully;
    /// otherwise, <see langword="null"/>.
    /// </returns>
    Task<string?> SendAsync(
    IEnumerable<string> recipients,
    string? subject,
    string text,
    IEnumerable<EmailAttachmentFile>? attachments,
    CancellationToken cancellationToken);
}
