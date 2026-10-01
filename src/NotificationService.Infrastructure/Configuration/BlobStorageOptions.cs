namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Settings for the blob container that stores attachments and voice audio ("BlobStorage" section).
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public required string ConnectionString { get; init; }

    public string ContainerName { get; init; } = "notifications";
}