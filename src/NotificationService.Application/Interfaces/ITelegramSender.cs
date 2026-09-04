namespace NotificationService.Application.Interfaces;

public interface ITelegramSender
{
    /// <summary>
    /// Sends an SMS notification to the specified recipient, optionally including an attachment.
    /// </summary>
    /// <param name="recipient">The phone number of the recipient.</param>
    /// <param name="text">The text content of the SMS.</param>
    /// <param name="attachment">The optional attachment stream to send with the message.</param>
    /// <param name="fileName">The optional file name of the attachment.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// A task containing the provider message ID if the message was sent successfully;
    /// otherwise, <see langword="null"/>.
    /// </returns>
    Task<string?> SendAsync(
    string recipient,
    string text,
    Stream? attachment,
    string? fileName,
    CancellationToken cancellationToken);
}