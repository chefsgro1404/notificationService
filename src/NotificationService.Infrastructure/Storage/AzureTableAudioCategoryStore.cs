using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Storage;

/// <summary>
/// Azure Table Storage implementation of <see cref="IAudioCategoryStore"/>. The table
/// (AuditStorage:AudioCategoryTableName, default "AudioCategory") is created if it does not exist;
/// category ids come from a counter row in the same table.
/// </summary>
public sealed class AzureTableAudioCategoryStore : IAudioCategoryStore
{
    /// <summary>Row key of the counter that issues category ids.</summary>
    internal const string CounterRowKey = "category-id";

    private readonly TableClient _table;
    private readonly TimeProvider _timeProvider;
    private volatile bool _tableReady;

    /// <summary>
    /// Creates the store from configuration.
    /// </summary>
    /// <param name="options">Table Storage connection string and table name.</param>
    /// <param name="timeProvider">Supplies creation and update times.</param>
    public AzureTableAudioCategoryStore(
        IOptions<AuditStorageOptions> options,
        TimeProvider timeProvider)
        : this(CreateTable(options.Value), timeProvider)
    {
    }

    internal AzureTableAudioCategoryStore(
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
            settings.AudioCategoryTableName);
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
    public async Task<AudioCategory> CreateAsync(
        string name,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var id = await TableIdCounter.NextAsync(_table, CounterRowKey, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        var entity = new AudioCategoryEntity
        {
            RowKey = AudioCategoryEntity.ToRowKey(id),
            CategoryId = id,
            Name = name,
            AudioId = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _table.AddEntityAsync(entity, cancellationToken);

        return ToModel(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioCategory>> ListAsync(
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var (rows, _) = await TableScan.ReadAsync<AudioCategoryEntity>(
            _table,
            TableClient.CreateQueryFilter($"PartitionKey eq {AudioCategoryEntity.Partition}"),
            TableScan.MaxRows,
            cancellationToken);

        return rows
            .OrderBy(x => x.CategoryId)
            .Select(ToModel)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<AudioCategory?> GetAsync(
        int categoryId,
        CancellationToken cancellationToken)
    {
        var entity = await FindAsync(categoryId, cancellationToken);

        return entity is null ? null : ToModel(entity);
    }

    /// <inheritdoc />
    public async Task<AudioCategory?> LinkAudioAsync(
        int categoryId,
        int audioId,
        CancellationToken cancellationToken)
    {
        var entity = await FindAsync(categoryId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        entity.AudioId = audioId;
        entity.UpdatedAtUtc = _timeProvider.GetUtcNow();

        // Relinking is last-writer-wins.
        await _table.UpdateEntityAsync(
            entity,
            ETag.All,
            TableUpdateMode.Replace,
            cancellationToken);

        return ToModel(entity);
    }

    private async Task<AudioCategoryEntity?> FindAsync(
        int categoryId,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var response = await _table.GetEntityIfExistsAsync<AudioCategoryEntity>(
            AudioCategoryEntity.Partition,
            AudioCategoryEntity.ToRowKey(categoryId),
            cancellationToken: cancellationToken);

        return response.HasValue ? response.Value : null;
    }

    private static AudioCategory ToModel(
        AudioCategoryEntity entity) =>
        new()
        {
            Id = entity.CategoryId,
            Name = entity.Name,
            AudioId = entity.AudioId,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
}
