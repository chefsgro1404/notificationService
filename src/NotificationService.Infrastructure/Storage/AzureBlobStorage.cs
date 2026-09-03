using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Storage;

public sealed class AzureBlobStorage : IBlobStorage
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorage(
        IOptions<BlobStorageOptions> options)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException(
                "BlobStorage:ConnectionString is not configured.");
        }

        _container = new BlobContainerClient(
            settings.ConnectionString,
            settings.ContainerName);
    }

    private async Task EnsureContainerAsync(
        CancellationToken cancellationToken)
    {
        await _container.CreateIfNotExistsAsync(
            cancellationToken: cancellationToken);
    }

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);

        var safeFileName = Path.GetFileName(fileName);

        var blobName =
            $"notifications/{notificationId}/{safeFileName}";

        var blob = _container.GetBlobClient(blobName);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            }
        };

        await blob.UploadAsync(
            content,
            options,
            cancellationToken);

        return blobName;
    }

    public async Task<Stream> OpenReadAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);

        return await blob.OpenReadAsync(
            cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);

        await blob.DeleteIfExistsAsync(
            cancellationToken: cancellationToken);
    }
}