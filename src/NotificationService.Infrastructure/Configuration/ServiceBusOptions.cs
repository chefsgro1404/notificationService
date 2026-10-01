namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Service Bus connection and queue settings ("ServiceBus" section).
/// </summary>
public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string? ConnectionString { get; init; }

    public string? Namespace { get; init; }

    public bool UseInMemory { get; init; }

    public string EmailQueue { get; init; } = "email";

    public string SmsQueue { get; init; } = "sms";

    public string TelegramQueue { get; init; } = "telegram";
}