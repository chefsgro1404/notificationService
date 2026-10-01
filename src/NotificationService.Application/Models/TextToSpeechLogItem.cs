using System.Text.Json.Serialization;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Models;

/// <summary>
/// One text-to-speech (T2A) record from the audio log.
/// </summary>
public sealed record TextToSpeechLogItem
{
    /// <summary>Sequential integer id of the audio.</summary>
    public required int Id { get; init; }

    /// <summary>Notification id of the request; the table row key and the audio file name.</summary>
    public required Guid NotificationId { get; init; }

    /// <summary>The text to convert.</summary>
    public required string Text { get; init; }

    /// <summary>Name of the blob that holds the audio ("T2A/{NotificationId}.wav"); empty until generated.</summary>
    public required string BlobName { get; init; }

    /// <summary>MIME type of the audio.</summary>
    public required string ContentType { get; init; }

    /// <summary>Size of the audio in bytes; 0 until generated.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Number of characters to convert.</summary>
    public required int CharacterCount { get; init; }

    /// <summary>Current status, serialized by name ("AudioNotGenerated", "Active", "Inactive", "Deleted").</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AudioStatus>))]
    public required AudioStatus Status { get; init; }

    /// <summary>Why generation failed, if it did.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>When the request was received (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>When the record last changed (UTC).</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Whether a WAV file exists for this record.</summary>
    [JsonIgnore]
    public bool HasAudio => !string.IsNullOrEmpty(BlobName);

    /// <summary>Read-only playback URL; only set for <see cref="AudioStatus.Active"/> audio.</summary>
    public Uri? AudioUrl { get; init; }

    /// <summary>When <see cref="AudioUrl"/> expires (UTC); only set with <see cref="AudioUrl"/>.</summary>
    public DateTimeOffset? AudioUrlExpiresAtUtc { get; init; }
}
