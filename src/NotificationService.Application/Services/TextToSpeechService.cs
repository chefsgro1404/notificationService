using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Application.Validation;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>
/// Default <see cref="ITextToSpeechService"/>. Each request is logged first as
/// <see cref="AudioStatus.AudioNotGenerated"/> with a new notification id and a sequential integer id; after
/// synthesis the WAV file is stored as "T2A/{notificationId}.wav" and the same record is updated to
/// <see cref="AudioStatus.Active"/>. Read-only URLs are valid for 60 minutes.
/// </summary>
public sealed class TextToSpeechService : ITextToSpeechService
{
    /// <summary>MIME type of the generated audio.</summary>
    public const string WavContentType = "audio/wav";

    /// <summary>Blob folder that holds text-to-audio files.</summary>
    public const string BlobFolder = "T2A";

    /// <summary>How long a returned audio URL remains valid.</summary>
    public static readonly TimeSpan AudioUrlValidity = TimeSpan.FromMinutes(60);

    private readonly ISpeechSynthesizer _synthesizer;
    private readonly IBlobStorage _blobStorage;
    private readonly ITextToSpeechLogStore _logStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="synthesizer">Converts text to WAV audio.</param>
    /// <param name="blobStorage">Stores the audio and creates read URLs.</param>
    /// <param name="logStore">Records requests and issues integer ids.</param>
    /// <param name="timeProvider">Supplies the current time for log times and URL expiry.</param>
    public TextToSpeechService(
        ISpeechSynthesizer synthesizer,
        IBlobStorage blobStorage,
        ITextToSpeechLogStore logStore,
        TimeProvider timeProvider)
    {
        _synthesizer = synthesizer;
        _blobStorage = blobStorage;
        _logStore = logStore;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Returns the blob name for a notification's audio: "T2A/{notificationId}.wav".
    /// </summary>
    /// <param name="notificationId">The notification id (the log row key).</param>
    /// <returns>The blob name.</returns>
    public static string BlobNameFor(Guid notificationId) =>
        $"{BlobFolder}/{notificationId}.wav";

    /// <summary>
    /// Checks that a blob name is a text-to-audio file made by this service ("T2A/{guid}.wav").
    /// </summary>
    /// <param name="blobName">The blob name to check.</param>
    /// <returns><see langword="true"/> when the name has the expected form.</returns>
    public static bool IsAudioBlobName(string? blobName) =>
        blobName is not null &&
        blobName.StartsWith(BlobFolder + "/", StringComparison.Ordinal) &&
        blobName.EndsWith(".wav", StringComparison.Ordinal) &&
        Guid.TryParseExact(blobName[(BlobFolder.Length + 1)..^4], "D", out _);

    /// <inheritdoc />
    public async Task<TextToSpeechResult> GenerateAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var normalized = SpeechText.Normalize(text);

        var error = SpeechText.Validate(normalized);

        if (error is not null)
        {
            throw new ArgumentException(error, nameof(text));
        }

        // 1. Log the request before generating anything.
        var now = _timeProvider.GetUtcNow();

        var pending = new TextToSpeechLogItem
        {
            Id = await _logStore.NextIdAsync(cancellationToken),
            NotificationId = Guid.NewGuid(),
            Text = normalized,
            BlobName = string.Empty,
            ContentType = WavContentType,
            SizeBytes = 0,
            CharacterCount = SpeechText.CountCharacters(normalized),
            Status = AudioStatus.AudioNotGenerated,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _logStore.AddAsync(pending, cancellationToken);

        // 2. Generate and store the audio.
        var blobName = BlobNameFor(pending.NotificationId);
        byte[] audio;

        try
        {
            audio = await _synthesizer.SynthesizeWavAsync(normalized, cancellationToken);

            using var content = new MemoryStream(audio, writable: false);

            await _blobStorage.UploadBlobAsync(
                content,
                blobName,
                WavContentType,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(pending, ex);
            throw;
        }

        // 3. Mark the same record as generated.
        var generated = pending with
        {
            BlobName = blobName,
            SizeBytes = audio.LongLength,
            Status = AudioStatus.Active,
            UpdatedAtUtc = _timeProvider.GetUtcNow()
        };

        await _logStore.UpdateAsync(generated, cancellationToken);

        var withUrl = WithAudioUrl(generated);

        return new TextToSpeechResult
        {
            Id = generated.Id,
            NotificationId = generated.NotificationId,
            BlobName = blobName,
            AudioUrl = withUrl.AudioUrl!,
            ExpiresAtUtc = withUrl.AudioUrlExpiresAtUtc!.Value,
            ContentType = WavContentType,
            SizeBytes = generated.SizeBytes,
            CharacterCount = generated.CharacterCount
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<TextToSpeechLogItem>> GetLogsAsync(
        TextToSpeechLogQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _logStore.QueryAsync(query, cancellationToken);

        return new PagedResult<TextToSpeechLogItem>
        {
            Items = page.Items.Select(WithAudioUrl).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            Truncated = page.Truncated
        };
    }

    /// <inheritdoc />
    public async Task<TextToSpeechLogItem?> GetLogAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var item = await _logStore.GetAsync(id, cancellationToken);

        return item is null ? null : WithAudioUrl(item);
    }

    /// <inheritdoc />
    public async Task<TextToSpeechLogItem?> UpdateStatusAsync(
        int id,
        AudioStatus status,
        CancellationToken cancellationToken)
    {
        if (status == AudioStatus.AudioNotGenerated)
        {
            throw new AudioStatusChangeException(
                "AudioNotGenerated is set automatically and cannot be chosen.");
        }

        var current = await _logStore.GetAsync(id, cancellationToken);

        if (current is null)
        {
            return null;
        }

        if (status != AudioStatus.Deleted && !current.HasAudio)
        {
            throw new AudioStatusChangeException(
                $"Audio {id} was never generated, so it can only be deleted.");
        }

        var item = await _logStore.UpdateStatusAsync(id, status, cancellationToken);

        return item is null ? null : WithAudioUrl(item);
    }

    /// <summary>
    /// Stores why generation failed. The record stays AudioNotGenerated; a failure to save the error is
    /// ignored so the original exception reaches the caller.
    /// </summary>
    private async Task RecordFailureAsync(
        TextToSpeechLogItem pending,
        Exception error)
    {
        try
        {
            await _logStore.UpdateAsync(
                pending with
                {
                    ErrorMessage = error is SpeechSynthesisException
                        ? error.Message
                        : "Audio generation failed unexpectedly.",
                    UpdatedAtUtc = _timeProvider.GetUtcNow()
                },
                CancellationToken.None);
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>
    /// Adds a fresh read-only URL to active audio; other statuses are returned without a URL.
    /// </summary>
    private TextToSpeechLogItem WithAudioUrl(TextToSpeechLogItem item)
    {
        if (item.Status != AudioStatus.Active || !item.HasAudio)
        {
            return item with { AudioUrl = null, AudioUrlExpiresAtUtc = null };
        }

        // Expiry is computed before the SAS is signed, so the reported time is never later than the real one.
        var expiresAtUtc = _timeProvider.GetUtcNow().Add(AudioUrlValidity);

        return item with
        {
            AudioUrl = _blobStorage.GetReadUri(item.BlobName, AudioUrlValidity),
            AudioUrlExpiresAtUtc = expiresAtUtc
        };
    }
}
