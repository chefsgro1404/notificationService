using Azure;
using Azure.Data.Tables;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Azure Table Storage row that records one text-to-speech (T2A) request.
/// Partition = <see cref="Partition"/>; row key = the notification id (a GUID), which is also the audio
/// file name: "T2A/{RowKey}.wav". <see cref="Id"/> is a separate sequential integer id.
/// </summary>
public sealed class TextToSpeechLogEntity : ITableEntity
{
    /// <summary>Partition that holds every audio log row.</summary>
    public const string Partition = "audio";

    public string PartitionKey { get; set; } = Partition;

    /// <summary>Notification id (GUID) of the request.</summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    /// <summary>Sequential integer id of the audio.</summary>
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>Blob that holds the audio; empty until the audio has been generated.</summary>
    public string BlobName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int CharacterCount { get; set; }

    /// <summary>Name of an <see cref="Enums.AudioStatus"/> value.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Why generation failed, when the status is still AudioNotGenerated.</summary>
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
