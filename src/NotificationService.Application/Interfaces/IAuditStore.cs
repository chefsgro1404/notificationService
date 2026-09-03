namespace NotificationService.Application.Interfaces;

public interface IAuditStore
{
    Task CreateAcceptedAsync(
        Guid notificationId,
        string channel,
        string recipient,
        string? blobName,
        CancellationToken cancellationToken);

    Task UpdateStatusAsync(
        Guid notificationId,
        string channel,
        string status,
        string? providerMessageId = null,
        string? errorCode = null,
        string? errorMessage = null,
        int? retryCount = null,
        CancellationToken cancellationToken = default);

    Task<bool> HasBeenProcessedAsync(
        Guid notificationId,
        string channel,
        CancellationToken cancellationToken);
}