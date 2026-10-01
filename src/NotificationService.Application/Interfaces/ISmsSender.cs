namespace NotificationService.Application.Interfaces;

/// <summary>
/// Defines operations for sending SMS notifications.
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Sends an SMS notification to the specified recipient.
    /// </summary>
    /// <param name="recipient">The phone number of the recipient.</param>
    /// <param name="text">The text content of the SMS.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// A task containing the provider message ID if the SMS was sent successfully;
    /// otherwise, <see langword="null"/>.
    /// </returns>
    Task<string?> SendAsync(
    string recipient,
    string text,
    CancellationToken cancellationToken);
}