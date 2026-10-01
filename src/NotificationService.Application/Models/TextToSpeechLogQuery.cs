using NotificationService.Domain.Enums;

namespace NotificationService.Application.Models;

/// <summary>
/// Filter and paging options for reading the text-to-speech audio log.
/// </summary>
public sealed class TextToSpeechLogQuery
{
    /// <summary>Statuses shown when the caller does not choose any: everything except deleted audio.</summary>
    public static readonly IReadOnlyCollection<AudioStatus> DefaultStatuses =
        [AudioStatus.AudioNotGenerated, AudioStatus.Active, AudioStatus.Inactive];

    /// <summary>Statuses to include; by default everything except deleted audio.</summary>
    public IReadOnlyCollection<AudioStatus> Statuses { get; init; } = DefaultStatuses;

    /// <summary>Case-insensitive text that the converted text must contain.</summary>
    public string? Search { get; init; }

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page.</summary>
    public int PageSize { get; init; } = PagedResult.DefaultPageSize;
}
