using NotificationService.Application.Models;

namespace NotificationService.Application.Interfaces;

/// <summary>
/// Persists audio categories and the audio each one is linked to.
/// </summary>
public interface IAudioCategoryStore
{
    /// <summary>
    /// Creates a category with the next integer id and no linked audio.
    /// </summary>
    /// <param name="name">The validated category name.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The new category.</returns>
    Task<AudioCategory> CreateAsync(
        string name,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads every category, ordered by id.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>All categories.</returns>
    Task<IReadOnlyList<AudioCategory>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one category.
    /// </summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The category, or <see langword="null"/> when it does not exist.</returns>
    Task<AudioCategory?> GetAsync(
        int categoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets the audio linked to a category, replacing any previous link.
    /// </summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="audioId">The audio id to link.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The updated category, or <see langword="null"/> when it does not exist.</returns>
    Task<AudioCategory?> LinkAudioAsync(
        int categoryId,
        int audioId,
        CancellationToken cancellationToken);
}
