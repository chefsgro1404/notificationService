using Azure;
using Azure.Data.Tables;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Azure Table Storage row for an audio category (partition = <see cref="Partition"/>, row key =
/// <see cref="ToRowKey"/> of <see cref="CategoryId"/>). A category points at the text-to-speech audio
/// (<see cref="AudioId"/>) that voice notifications in that category play.
/// </summary>
public sealed class AudioCategoryEntity : ITableEntity
{
    /// <summary>Partition that holds every category row.</summary>
    public const string Partition = "category";

    public string PartitionKey { get; set; } = Partition;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    /// <summary>Sequential integer id of the category.</summary>
    public int CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Id of the linked text-to-speech audio, or <see langword="null"/> when none is linked.</summary>
    public int? AudioId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Converts a category id to its zero-padded row key, so rows sort by id.
    /// </summary>
    /// <param name="categoryId">The category id.</param>
    /// <returns>The 10-digit row key.</returns>
    public static string ToRowKey(int categoryId) =>
        categoryId.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
}
