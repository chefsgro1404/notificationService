namespace NotificationService.Application.Models;

/// <summary>
/// Describes a generated WAV file stored in blob storage and the temporary URL used to play it.
/// </summary>
public sealed class TextToSpeechResult
{
    /// <summary>Sequential integer id of the generated audio (its key in the text-to-speech log).</summary>
    public required int Id { get; init; }

    /// <summary>Notification id of the request; the audio is stored as "T2A/{NotificationId}.wav".</summary>
    public required Guid NotificationId { get; init; }

    /// <summary>Name of the blob that holds the audio, relative to the container.</summary>
    public required string BlobName { get; init; }

    /// <summary>Read-only SAS URL for the audio file.</summary>
    public required Uri AudioUrl { get; init; }

    /// <summary>The time (UTC) after which <see cref="AudioUrl"/> stops working.</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>MIME type of the audio file.</summary>
    public required string ContentType { get; init; }

    /// <summary>Size of the audio file in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Number of characters (Unicode code points) that were converted.</summary>
    public required int CharacterCount { get; init; }
}
