using NotificationService.Application.Models;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Interfaces;

/// <summary>
/// Persists the log of generated text-to-speech audio and hands out sequential integer ids.
/// </summary>
public interface ITextToSpeechLogStore
{
    /// <summary>
    /// Reserves the next integer id. Ids are unique and increasing; a reserved id that is never used leaves a gap.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The new id (1 or greater).</returns>
    Task<int> NextIdAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new log record (normally with status AudioNotGenerated, before synthesis starts).
    /// </summary>
    /// <param name="item">The record to add; <see cref="TextToSpeechLogItem.AudioUrl"/> is not stored.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddAsync(
        TextToSpeechLogItem item,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an existing log record, identified by <see cref="TextToSpeechLogItem.NotificationId"/>.
    /// Used to record the generated audio (or the failure) after synthesis.
    /// </summary>
    /// <param name="item">The complete record; <see cref="TextToSpeechLogItem.AudioUrl"/> is not stored.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateAsync(
        TextToSpeechLogItem item,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one log record.
    /// </summary>
    /// <param name="id">The audio id.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The record, or <see langword="null"/> when no audio has that id.</returns>
    Task<TextToSpeechLogItem?> GetAsync(
        int id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Changes the status of a log record.
    /// </summary>
    /// <param name="id">The audio id.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The updated record, or <see langword="null"/> when no audio has that id.</returns>
    Task<TextToSpeechLogItem?> UpdateStatusAsync(
        int id,
        AudioStatus status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a filtered page of log records, newest first.
    /// </summary>
    /// <param name="query">Filters and paging.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The requested page.</returns>
    Task<PagedResult<TextToSpeechLogItem>> QueryAsync(
        TextToSpeechLogQuery query,
        CancellationToken cancellationToken);
}
