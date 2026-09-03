using NotificationService.Application.Models;

namespace NotificationService.Application.Interfaces;

public interface INotificationPublisher
{
    Task PublishAsync(
        NotificationMessage message,
        CancellationToken cancellationToken);
}