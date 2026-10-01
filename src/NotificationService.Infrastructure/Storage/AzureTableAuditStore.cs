using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Storage;

/// <summary>
/// Azure Table Storage implementation of <see cref="IAuditStore"/>.
/// </summary>
public sealed class AzureTableAuditStore : IAuditStore
{
    private readonly TableClient _table;

    public AzureTableAuditStore(
        IOptions<AuditStorageOptions> options)
        : this(CreateTable(options.Value))
    {
    }

    internal AzureTableAuditStore(
        TableClient table)
    {
        _table = table;
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
            settings.TableName);
    }

    private async Task EnsureTableAsync(
        CancellationToken cancellationToken)
    {
        await _table.CreateIfNotExistsAsync(
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task CreateAcceptedAsync(
        Guid notificationId,
        string channel,
        string recipient,
        string? blobName,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var entity = new NotificationAuditEntity
        {
            PartitionKey = channel,
            RowKey = notificationId.ToString(),

            Channel = channel,
            Recipient = recipient,
            Status = "Accepted",

            CreatedAtUtc = now,
            UpdatedAtUtc = now,

            BlobName = blobName,
            RetryCount = 0
        };

        await _table.AddEntityAsync(
            entity,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(
    Guid notificationId,
    string channel,
    string status,
    string? providerMessageId = null,
    string? errorCode = null,
    string? errorMessage = null,
    int? retryCount = null,
    CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken);

        var partitionKey = channel;
        var rowKey = notificationId.ToString();

        var entity = await _table.GetEntityAsync<NotificationAuditEntity>(
            partitionKey,
            rowKey,
            cancellationToken: cancellationToken);

        entity.Value.Status = status;
        entity.Value.UpdatedAtUtc = DateTime.UtcNow;

        if (providerMessageId is not null)
            entity.Value.ProviderMessageId = providerMessageId;

        if (errorCode is not null)
            entity.Value.ErrorCode = errorCode;

        if (errorMessage is not null)
            entity.Value.ErrorMessage = errorMessage;

        if (retryCount.HasValue)
            entity.Value.RetryCount = retryCount.Value;

        await _table.UpdateEntityAsync(
            entity.Value,
            entity.Value.ETag,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasBeenProcessedAsync(
        Guid notificationId,
        string channel,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        try
        {
            var result = await _table.GetEntityAsync<NotificationAuditEntity>(
                channel,
                notificationId.ToString(),
                cancellationToken: cancellationToken);

            return result.Value.Status == "Sent";
        }
        catch (Azure.RequestFailedException ex)
            when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<PagedResult<NotificationLogItem>> QueryAsync(
        NotificationLogQuery query,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        // Channel, status and date range are filtered by Table Storage; the recipient "contains"
        // filter and newest-first ordering are applied in memory.
        var filter = TableScan.And(
            query.Channel is { } channel
                ? TableClient.CreateQueryFilter($"PartitionKey eq {channel.ToString()}")
                : null,
            query.Status is { } status
                ? TableClient.CreateQueryFilter($"Status eq {status.ToString()}")
                : null,
            query.FromUtc is { } from
                ? TableClient.CreateQueryFilter($"CreatedAtUtc ge {from.UtcDateTime}")
                : null,
            query.ToUtc is { } to
                ? TableClient.CreateQueryFilter($"CreatedAtUtc lt {to.UtcDateTime}")
                : null);

        var (rows, truncated) = await TableScan.ReadAsync<NotificationAuditEntity>(
            _table,
            filter,
            TableScan.MaxRows,
            cancellationToken);

        var recipient = query.Recipient?.Trim();

        var items = rows
            .Where(x => string.IsNullOrEmpty(recipient) ||
                        x.Recipient.Contains(recipient, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(ToLogItem)
            .ToList();

        return PagedResult.Create(
            items,
            query.Page,
            query.PageSize,
            truncated);
    }

    private static NotificationLogItem ToLogItem(
        NotificationAuditEntity entity) =>
        new()
        {
            Id = entity.RowKey,
            Channel = entity.Channel,
            Recipient = entity.Recipient,
            Status = entity.Status,
            CreatedAtUtc = AsUtc(entity.CreatedAtUtc),
            UpdatedAtUtc = AsUtc(entity.UpdatedAtUtc),
            ProviderMessageId = entity.ProviderMessageId,
            ErrorCode = entity.ErrorCode,
            ErrorMessage = entity.ErrorMessage,
            RetryCount = entity.RetryCount,
            HasAttachment = !string.IsNullOrEmpty(entity.BlobName)
        };

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}