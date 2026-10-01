namespace NotificationService.Application.Interfaces;

/// <summary>
/// Defines operations for storing, reading, and deleting files in blob storage.
/// </summary>
public interface IBlobStorage
{
    /// <summary>
    /// Uploads a file to blob storage for the specified notification.
    /// </summary>
    /// <param name="content">The stream containing the file content.</param>
    /// <param name="fileName">The name of the file to upload.</param>
    /// <param name="contentType">The MIME type of the file.</param>
    /// <param name="notificationId">The unique identifier of the notification associated with the file.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The name of the uploaded blob.</returns>
    Task<string> UploadAsync(
    Stream content,
    string fileName,
    string contentType,
    Guid notificationId,
    CancellationToken cancellationToken);

    /// <summary>
    /// Uploads content to an exact blob name, replacing any existing blob with that name.
    /// </summary>
    /// <param name="content">The stream containing the file content.</param>
    /// <param name="blobName">The full blob name within the container, for example "tts/2026/09/30/id.wav".</param>
    /// <param name="contentType">The MIME type of the file.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous upload.</returns>
    Task UploadBlobAsync(
        Stream content,
        string blobName,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a blob for reading.
    /// </summary>
    /// <param name="blobName">The name of the blob to read.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A readable stream containing the blob content.</returns>
    Task<Stream> OpenReadAsync(
        string blobName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a blob from storage.
    /// </summary>
    /// <param name="blobName">The name of the blob to delete.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    Task DeleteAsync(
        string blobName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a time-limited, read-only URL for a blob.
    /// </summary>
    /// <param name="blobName">The name of the blob.</param>
    /// <param name="validFor">How long the URL remains valid.</param>
    /// <returns>A read-only URL for the blob.</returns>
    Uri GetReadUri(
        string blobName,
        TimeSpan validFor);
}