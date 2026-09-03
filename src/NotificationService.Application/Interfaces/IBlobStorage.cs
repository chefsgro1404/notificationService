namespace NotificationService.Application.Interfaces;

public interface IBlobStorage
{
    Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        Guid notificationId,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        string blobName,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string blobName,
        CancellationToken cancellationToken);
}