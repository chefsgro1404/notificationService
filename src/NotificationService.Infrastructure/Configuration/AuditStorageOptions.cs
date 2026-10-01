namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Settings for the Azure Table Storage account holding the notification audit table ("AuditStorage" section).
/// The same account also holds the text-to-speech log and audio category tables, whose names are fixed in code.
/// </summary>
public sealed class AuditStorageOptions
{
    public const string SectionName = "AuditStorage";

    public string ConnectionString { get; init; } = string.Empty;

    public string TableName { get; init; } = "NotificationAudit";
}