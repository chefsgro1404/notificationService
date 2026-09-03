namespace NotificationService.Infrastructure.Configuration;

public sealed class AuditStorageOptions
{
    public const string SectionName = "AuditStorage";

    public string ConnectionString { get; init; } = string.Empty;

    public string TableName { get; init; } = "NotificationAudit";
}