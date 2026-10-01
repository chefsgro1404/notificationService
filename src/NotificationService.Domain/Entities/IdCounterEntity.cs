using Azure;
using Azure.Data.Tables;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Azure Table Storage row holding the last integer id handed out for a table.
/// Updated with optimistic concurrency (ETag) so concurrent writers never receive the same id.
/// </summary>
public sealed class IdCounterEntity : ITableEntity
{
    /// <summary>Partition used for counter rows; kept separate from data rows.</summary>
    public const string Partition = "counter";

    public string PartitionKey { get; set; } = Partition;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    /// <summary>The most recently issued id.</summary>
    public int LastId { get; set; }
}
