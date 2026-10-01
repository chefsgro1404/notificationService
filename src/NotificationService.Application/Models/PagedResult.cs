namespace NotificationService.Application.Models;

/// <summary>
/// One page of results plus the information a client needs to render pagination.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>Items on this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>1-based page number.</summary>
    public required int Page { get; init; }

    /// <summary>Maximum number of items per page.</summary>
    public required int PageSize { get; init; }

    /// <summary>Number of items that matched the filter (across all pages).</summary>
    public required int TotalCount { get; init; }

    /// <summary>Number of pages; 0 when nothing matched.</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>
    /// <see langword="true"/> when the scan stopped at its row limit, so <see cref="TotalCount"/> is a lower bound
    /// and the filter should be narrowed (for example with a date range).
    /// </summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Creates <see cref="PagedResult{T}"/> instances from fully sorted, filtered lists.
/// </summary>
public static class PagedResult
{
    /// <summary>Largest page size a client may request.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Page size used when the client does not ask for one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Cuts one page out of an already filtered and sorted list.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">All matching items, in display order.</param>
    /// <param name="page">1-based page number; a page past the end returns no items.</param>
    /// <param name="pageSize">Items per page (1 to <see cref="MaxPageSize"/>).</param>
    /// <param name="truncated">Whether <paramref name="items"/> was cut off at a scan limit.</param>
    /// <returns>The requested page.</returns>
    public static PagedResult<T> Create<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        bool truncated = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        return new PagedResult<T>
        {
            Items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = items.Count,
            Truncated = truncated
        };
    }
}
