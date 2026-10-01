using NotificationService.Domain.Enums;

namespace NotificationService.Application.Models;

/// <summary>
/// Filter and paging options for reading the notification audit log. Every filter is optional.
/// </summary>
public sealed class NotificationLogQuery
{
    /// <summary>Only notifications sent on this channel.</summary>
    public NotificationChannel? Channel { get; init; }

    /// <summary>Only notifications currently in this status.</summary>
    public NotificationStatus? Status { get; init; }

    /// <summary>Case-insensitive text that the recipient must contain.</summary>
    public string? Recipient { get; init; }

    /// <summary>Only notifications created at or after this time (UTC).</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>Only notifications created before this time (UTC).</summary>
    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page.</summary>
    public int PageSize { get; init; } = PagedResult.DefaultPageSize;
}
