namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Settings for the Azure Table Storage account holding the notification audit table and the
/// text-to-speech log and audio category tables ("AuditStorage" section). Tables are created if they do not exist.
/// </summary>
public sealed class AuditStorageOptions
{
    public const string SectionName = "AuditStorage";

    public string ConnectionString { get; init; } = string.Empty;

    public string TableName { get; init; } = "NotificationAudit";

    /// <summary>Table that logs generated text-to-speech audio (integer ids, active/inactive/deleted status).</summary>
    public string TextToSpeechTableName { get; init; } = "TextToSpeechLog";

    /// <summary>Table of audio categories (integer id, name, linked audio id).</summary>
    public string AudioCategoryTableName { get; init; } = "AudioCategory";
}