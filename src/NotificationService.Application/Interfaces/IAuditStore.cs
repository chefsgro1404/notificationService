namespace NotificationService.Application.Interfaces;

/// <summary>
/// Defines operations for storing and updating notification audit records.
/// </summary>
public interface IAuditStore
{
    /// <summary>
    /// Creates an audit record for an accepted notification.
    /// </summary>
    /// <param name="notificationId">The unique identifier of the notification.</param>
    /// <param name="channel">The notification channel, such as Email, SMS, or Telegram.</param>
    /// <param name="recipient">The recipient of the notification.</param>
    /// <param name="blobName">The optional name of the blob containing notification attachments.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CreateAcceptedAsync(
    Guid notificationId,
    string channel,
    string recipient,
    string? blobName,
    CancellationToken cancellationToken);

    /// <summary>
    /// Updates the processing status of a notification audit record.
    /// </summary>
    /// <param name="notificationId">The unique identifier of the notification.</param>
    /// <param name="channel">The notification channel, such as Email, SMS, or Telegram.</param>
    /// <param name="status">The current processing status of the notification.</param>
    /// <param name="providerMessageId">The optional message identifier returned by the notification provider.</param>
    /// <param name="errorCode">The optional error code returned when processing fails.</param>
    /// <param name="errorMessage">The optional error message describing the failure.</param>
    /// <param name="retryCount">The optional number of times the notification has been retried.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateStatusAsync(
        Guid notificationId,
        string channel,
        string status,
        string? providerMessageId = null,
        string? errorCode = null,
        string? errorMessage = null,
        int? retryCount = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a notification has already been processed for the specified channel.
    /// </summary>
    /// <param name="notificationId">The unique identifier of the notification.</param>
    /// <param name="channel">The notification channel, such as Email, SMS, or Telegram.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// A task containing <see langword="true"/> if the notification has already been processed;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    Task<bool> HasBeenProcessedAsync(
        Guid notificationId,
        string channel,
        CancellationToken cancellationToken);       
}