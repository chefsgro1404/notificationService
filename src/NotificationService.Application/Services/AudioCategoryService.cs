using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>
/// Default <see cref="IAudioCategoryService"/>: validates category names, only links active audio, and
/// resolves a category to the audio blob that a voice call plays.
/// </summary>
public sealed class AudioCategoryService : IAudioCategoryService
{
    /// <summary>Maximum length of a category name.</summary>
    public const int MaxNameLength = 50;

    private readonly IAudioCategoryStore _categories;
    private readonly ITextToSpeechLogStore _audioLog;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="categories">Stores categories.</param>
    /// <param name="audioLog">Reads text-to-speech audio records.</param>
    public AudioCategoryService(
        IAudioCategoryStore categories,
        ITextToSpeechLogStore audioLog)
    {
        _categories = categories;
        _audioLog = audioLog;
    }

    /// <inheritdoc />
    public async Task<AudioCategory> CreateAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Category name must be 1 to {MaxNameLength} characters.",
                nameof(name));
        }

        var existing = await _categories.ListAsync(cancellationToken);

        if (existing.Any(x => string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AudioCategoryException($"A category named \"{trimmed}\" already exists.");
        }

        return await _categories.CreateAsync(trimmed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioCategory>> ListAsync(
        CancellationToken cancellationToken) =>
        (await _categories.ListAsync(cancellationToken))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <inheritdoc />
    public async Task<AudioCategory?> LinkAudioAsync(
        int categoryId,
        int audioId,
        CancellationToken cancellationToken)
    {
        if (await _categories.GetAsync(categoryId, cancellationToken) is null)
        {
            return null;
        }

        var audio = await _audioLog.GetAsync(audioId, cancellationToken);

        if (audio is null)
        {
            throw new AudioCategoryException($"Audio {audioId} was not found.");
        }

        if (audio.Status != AudioStatus.Active || !audio.HasAudio)
        {
            throw new AudioCategoryException($"Only active audio can be linked; audio {audioId} is {audio.Status}.");
        }

        return await _categories.LinkAudioAsync(categoryId, audioId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> ResolveAudioBlobAsync(
        int categoryId,
        CancellationToken cancellationToken)
    {
        var category = await _categories.GetAsync(categoryId, cancellationToken)
            ?? throw new AudioCategoryException($"Category {categoryId} was not found.");

        if (category.AudioId is not { } audioId)
        {
            throw new AudioCategoryException($"Category \"{category.Name}\" has no linked audio.");
        }

        var audio = await _audioLog.GetAsync(audioId, cancellationToken);

        if (audio is null || audio.Status != AudioStatus.Active || !audio.HasAudio)
        {
            throw new AudioCategoryException(
                $"The audio linked to category \"{category.Name}\" (audio {audioId}) is not active.");
        }

        return audio.BlobName;
    }
}
