using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Storage;

/// <summary>
/// Azure Table Storage implementation of <see cref="ITextToSpeechLogStore"/>. The <see cref="TableName"/> table,
/// in the AuditStorage account, is created before the first operation if it does not exist.
/// Rows are keyed by notification id; integer ids come from a counter row updated with optimistic
/// concurrency and are looked up with a filter on the Id column.
/// </summary>
public sealed class AzureTableTextToSpeechLogStore : ITextToSpeechLogStore
{
    /// <summary>Name of the text-to-speech log table.</summary>
    public const string TableName = "TextToSpeechLog";

    /// <summary>Row key of the counter that issues audio ids.</summary>
    internal const string CounterRowKey = "audio-id";

    private readonly TableClient _table;
    private readonly TimeProvider _timeProvider;
    private volatile bool _tableReady;

    /// <summary>
    /// Creates the store from configuration.
    /// </summary>
    /// <param name="options">Table Storage connection string and table name.</param>
    /// <param name="timeProvider">Supplies the time recorded when a status changes.</param>
    public AzureTableTextToSpeechLogStore(
        IOptions<AuditStorageOptions> options,
        TimeProvider timeProvider)
        : this(CreateTable(options.Value), timeProvider)
    {
    }

    internal AzureTableTextToSpeechLogStore(
        TableClient table,
        TimeProvider timeProvider)
    {
        _table = table;
        _timeProvider = timeProvider;
    }

    private static TableClient CreateTable(
        AuditStorageOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException(
                "AuditStorage:ConnectionString is not configured.");
        }

        return new TableClient(
            settings.ConnectionString,
            TableName);
    }

    private async Task EnsureTableAsync(
        CancellationToken cancellationToken)
    {
        if (_tableReady)
        {
            return;
        }

        await _table.CreateIfNotExistsAsync(cancellationToken);

        _tableReady = true;
    }

    /// <inheritdoc />
    public async Task<int> NextIdAsync(
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        return await TableIdCounter.NextAsync(_table, CounterRowKey, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(
        TextToSpeechLogItem item,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        await _table.AddEntityAsync(
            ToEntity(item),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        TextToSpeechLogItem item,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        await _table.UpdateEntityAsync(
            ToEntity(item),
            ETag.All,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TextToSpeechLogItem?> GetAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken);

        return entity is null ? null : ToItem(entity);
    }

    /// <inheritdoc />
    public async Task<TextToSpeechLogItem?> UpdateStatusAsync(
        int id,
        AudioStatus status,
        CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        entity.Status = status.ToString();
        entity.UpdatedAtUtc = _timeProvider.GetUtcNow();

        // Status changes are last-writer-wins.
        await _table.UpdateEntityAsync(
            entity,
            ETag.All,
            TableUpdateMode.Replace,
            cancellationToken);

        return ToItem(entity);
    }

    /// <inheritdoc />
    public async Task<PagedResult<TextToSpeechLogItem>> QueryAsync(
        TextToSpeechLogQuery query,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var statusFilter = string.Join(
            " or ",
            query.Statuses
                .Distinct()
                .Select(x => TableClient.CreateQueryFilter($"Status eq {x.ToString()}")));

        var filter = TableScan.And(
            TableClient.CreateQueryFilter($"PartitionKey eq {TextToSpeechLogEntity.Partition}"),
            statusFilter);

        var (rows, truncated) = await TableScan.ReadAsync<TextToSpeechLogEntity>(
            _table,
            filter,
            TableScan.MaxRows,
            cancellationToken);

        var search = query.Search?.Trim();

        var items = rows
            .Where(x => string.IsNullOrEmpty(search) ||
                        x.Text.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Id)
            .Select(ToItem)
            .ToList();

        return PagedResult.Create(
            items,
            query.Page,
            query.PageSize,
            truncated);
    }

    private async Task<TextToSpeechLogEntity?> FindAsync(
        int id,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {TextToSpeechLogEntity.Partition} and Id eq {id}");

        var (rows, _) = await TableScan.ReadAsync<TextToSpeechLogEntity>(
            _table,
            filter,
            maxRows: 1,
            cancellationToken);

        return rows.FirstOrDefault();
    }

    private static TextToSpeechLogEntity ToEntity(
        TextToSpeechLogItem item) =>
        new()
        {
            RowKey = item.NotificationId.ToString(),
            Id = item.Id,
            Text = item.Text,
            BlobName = item.BlobName,
            ContentType = item.ContentType,
            SizeBytes = item.SizeBytes,
            CharacterCount = item.CharacterCount,
            Status = item.Status.ToString(),
            ErrorMessage = item.ErrorMessage,
            CreatedAtUtc = item.CreatedAtUtc,
            UpdatedAtUtc = item.UpdatedAtUtc
        };

    private static TextToSpeechLogItem ToItem(
        TextToSpeechLogEntity entity) =>
        new()
        {
            Id = entity.Id,
            NotificationId = Guid.TryParse(entity.RowKey, out var notificationId) ? notificationId : Guid.Empty,
            Text = entity.Text,
            BlobName = entity.BlobName,
            ContentType = entity.ContentType,
            SizeBytes = entity.SizeBytes,
            CharacterCount = entity.CharacterCount,
            Status = Enum.TryParse<AudioStatus>(entity.Status, ignoreCase: true, out var status)
                ? status
                : AudioStatus.Inactive,
            ErrorMessage = entity.ErrorMessage,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
}
