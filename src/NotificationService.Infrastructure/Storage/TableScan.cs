using Azure.Data.Tables;

namespace NotificationService.Infrastructure.Storage;

/// <summary>
/// Reads Azure Table query results up to a fixed row limit. Table Storage cannot sort or count, so log
/// pages are built by reading the (server-side filtered) rows, then sorting and paging them in memory;
/// the limit bounds the cost of a single request.
/// </summary>
internal static class TableScan
{
    /// <summary>Maximum rows read for one log query.</summary>
    public const int MaxRows = 5000;

    private const int PageSize = 1000;

    /// <summary>
    /// Reads rows that match the filter, stopping after <paramref name="maxRows"/>.
    /// </summary>
    /// <typeparam name="T">The table entity type.</typeparam>
    /// <param name="table">The table to query.</param>
    /// <param name="filter">OData filter built with <see cref="TableClient.CreateQueryFilter(FormattableString)"/>, or <see langword="null"/> for all rows.</param>
    /// <param name="maxRows">Maximum rows to return.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The rows read, and whether more rows matched than were returned.</returns>
    public static async Task<(List<T> Rows, bool Truncated)> ReadAsync<T>(
        TableClient table,
        string? filter,
        int maxRows,
        CancellationToken cancellationToken)
        where T : class, ITableEntity
    {
        var rows = new List<T>();

        await foreach (var row in table.QueryAsync<T>(filter, PageSize, cancellationToken: cancellationToken))
        {
            if (rows.Count == maxRows)
            {
                return (rows, true);
            }

            rows.Add(row);
        }

        return (rows, false);
    }

    /// <summary>
    /// Joins non-empty filter clauses with "and", wrapping each in parentheses.
    /// </summary>
    /// <param name="clauses">Filter clauses; <see langword="null"/> entries are skipped.</param>
    /// <returns>The combined filter, or <see langword="null"/> when there are no clauses.</returns>
    public static string? And(params string?[] clauses)
    {
        var parts = clauses.Where(x => !string.IsNullOrEmpty(x)).ToList();

        return parts.Count == 0
            ? null
            : string.Join(" and ", parts.Select(x => $"({x})"));
    }
}
