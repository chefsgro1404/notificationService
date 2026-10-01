using NotificationService.Application.Models;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Interfaces;

/// <summary>
/// Turns text into a WAV file, stores and logs it, and manages the generated audio afterwards.
/// </summary>
public interface ITextToSpeechService
{
    /// <summary>
    /// Records the request in the text-to-speech log as <see cref="AudioStatus.AudioNotGenerated"/>, synthesizes
    /// the text, uploads the WAV file to "T2A/{notificationId}.wav", marks the record
    /// <see cref="AudioStatus.Active"/>, and creates a read-only URL. On failure the record keeps
    /// <see cref="AudioStatus.AudioNotGenerated"/> with an error message.
    /// </summary>
    /// <param name="text">The raw text; it is normalized and validated before synthesis.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>Details of the stored audio, including its integer id and temporary URL.</returns>
    /// <exception cref="ArgumentException">The text is empty or longer than the allowed maximum.</exception>
    /// <exception cref="Exceptions.SpeechSynthesisException">The speech service failed.</exception>
    Task<TextToSpeechResult> GenerateAsync(
        string text,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a filtered page of the audio log. Active items include a fresh playback URL.
    /// </summary>
    /// <param name="query">Filters and paging.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The requested page.</returns>
    Task<PagedResult<TextToSpeechLogItem>> GetLogsAsync(
        TextToSpeechLogQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one audio log record. An active item includes a fresh playback URL.
    /// </summary>
    /// <param name="id">The audio id.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The record, or <see langword="null"/> when no audio has that id.</returns>
    Task<TextToSpeechLogItem?> GetLogAsync(
        int id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks audio as active, inactive, or deleted (soft delete; the file and record are kept).
    /// Audio that was never generated can only be deleted.
    /// </summary>
    /// <param name="id">The audio id.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The updated record, or <see langword="null"/> when no audio has that id.</returns>
    /// <exception cref="Exceptions.AudioStatusChangeException">The change is not allowed.</exception>
    Task<TextToSpeechLogItem?> UpdateStatusAsync(
        int id,
        AudioStatus status,
        CancellationToken cancellationToken);
}
