namespace NotificationService.Infrastructure.Configuration;

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public required string ConnectionString { get; init; }

    public string ContainerName { get; init; } = "notifications";
}