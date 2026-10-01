using NotificationService.Application.Models;

namespace NotificationService.Application.Interfaces;

/// <summary>
/// Manages audio categories and resolves which audio file a voice notification should play.
/// </summary>
public interface IAudioCategoryService
{
    /// <summary>
    /// Creates a category.
    /// </summary>
    /// <param name="name">The raw name; it is trimmed and must be 1–50 characters and unique.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The new category.</returns>
    /// <exception cref="ArgumentException">The name is empty or too long.</exception>
    /// <exception cref="Exceptions.AudioCategoryException">A category with that name already exists.</exception>
    Task<AudioCategory> CreateAsync(
        string? name,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads every category, ordered by name.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>All categories.</returns>
    Task<IReadOnlyList<AudioCategory>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Links active audio to a category, replacing any previous link.
    /// </summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="audioId">The audio id.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The updated category, or <see langword="null"/> when the category does not exist.</returns>
    /// <exception cref="Exceptions.AudioCategoryException">The audio does not exist or is not active.</exception>
    Task<AudioCategory?> LinkAudioAsync(
        int categoryId,
        int audioId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the audio file to play for a category: the category must exist and be linked to active audio.
    /// </summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The blob name of the audio, for example "T2A/{notificationId}.wav".</returns>
    /// <exception cref="Exceptions.AudioCategoryException">The category or its audio cannot be used.</exception>
    Task<string> ResolveAudioBlobAsync(
        int categoryId,
        CancellationToken cancellationToken);
}
