namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Azure Communication Services settings for email and voice ("AzureCommunicationServices" section).
/// </summary>
public sealed class AzureCommunicationServicesOptions
{
    public const string SectionName =
        "AzureCommunicationServices";

    public string ConnectionString { get; init; } =
        string.Empty;

    public string SenderAddress { get; init; } =
        string.Empty;

    /// <summary>ACS phone number (E.164) used as caller id for voice calls.</summary>
    public string CallerPhoneNumber { get; init; } =
        string.Empty;

    /// <summary>Public HTTPS base URL of this Function App; ACS posts call events here.</summary>
    public string CallbackBaseUrl { get; init; } =
        string.Empty;
}