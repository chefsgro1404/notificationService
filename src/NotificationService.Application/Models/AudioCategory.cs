namespace NotificationService.Application.Models;

/// <summary>
/// An audio category: a named slot that points at the text-to-speech audio played by voice
/// notifications sent with that category.
/// </summary>
public sealed record AudioCategory
{
    /// <summary>Sequential integer id of the category.</summary>
    public required int Id { get; init; }

    /// <summary>Display name, unique (case-insensitive).</summary>
    public required string Name { get; init; }

    /// <summary>Id of the linked audio, or <see langword="null"/> when none is linked yet.</summary>
    public int? AudioId { get; init; }

    /// <summary>When the category was created (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>When the category last changed (UTC).</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>
/// JSON body for creating a category, for example <c>{ "name": "Support" }</c>.
/// </summary>
public sealed class CreateAudioCategoryRequest
{
    /// <summary>The category name (1–50 characters).</summary>
    public string? Name { get; init; }
}

/// <summary>
/// JSON body for linking audio to a category, for example <c>{ "audioId": 12 }</c>.
/// </summary>
public sealed class LinkAudioRequest
{
    /// <summary>Id of the active audio to link.</summary>
    public int? AudioId { get; init; }
}
