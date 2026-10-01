using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IBlobStorage"/>.
/// </summary>
public sealed class AzureBlobStorage : IBlobStorage
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorage(
        IOptions<BlobStorageOptions> options)
        : this(CreateContainer(options.Value))
    {
    }

    internal AzureBlobStorage(
        BlobContainerClient container)
    {
        _container = container;
    }

    private static BlobContainerClient CreateContainer(
        BlobStorageOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException(
                "BlobStorage:ConnectionString is not configured.");
        }

        return new BlobContainerClient(
            settings.ConnectionString,
            settings.ContainerName);
    }

    private async Task EnsureContainerAsync(
        CancellationToken cancellationToken)
    {
        await _container.CreateIfNotExistsAsync(
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var safeFileName = Path.GetFileName(fileName);

        var blobName =
            $"notifications/{notificationId}/{safeFileName}";

        await UploadBlobAsync(
            content,
            blobName,
            contentType,
            cancellationToken);

        return blobName;
    }

    /// <inheritdoc />
    public async Task UploadBlobAsync(
        Stream content,
        string blobName,
        string contentType,
        CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);

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
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);

        return await blob.OpenReadAsync(
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);

        await blob.DeleteIfExistsAsync(
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Uri GetReadUri(
        string blobName,
        TimeSpan validFor)
    {
        var blob = _container.GetBlobClient(blobName);

        if (!blob.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "BlobStorage:ConnectionString must include an account key to generate read URLs.");
        }

        return blob.GenerateSasUri(
            BlobSasPermissions.Read,
            DateTimeOffset.UtcNow.Add(validFor));
    }
}