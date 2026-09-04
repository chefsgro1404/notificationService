using NotificationService.Application.Models;

namespace NotificationService.Application.Interfaces;

/// <summary>
/// Defines operations for publishing notification messages.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Publishes a notification message for asynchronous processing.
    /// </summary>
    /// <param name="message">The notification message to publish.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishAsync(
    NotificationMessage message,
    CancellationToken cancellationToken);
}
