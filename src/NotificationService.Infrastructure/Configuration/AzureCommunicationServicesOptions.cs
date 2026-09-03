namespace NotificationService.Infrastructure.Configuration;

public sealed class AzureCommunicationServicesOptions
{
    public const string SectionName =
        "AzureCommunicationServices";

    public string ConnectionString { get; init; } =
        string.Empty;

    public string SenderAddress { get; init; } =
        string.Empty;
}