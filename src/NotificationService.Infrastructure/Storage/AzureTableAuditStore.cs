using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Storage;

public sealed class AzureTableAuditStore : IAuditStore
{
    private readonly TableClient _table;

    public AzureTableAuditStore(
        IOptions<AuditStorageOptions> options)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException(
                "AuditStorage:ConnectionString is not configured.");
        }

        _table = new TableClient(
            settings.ConnectionString,
            settings.TableName);
    }

    private async Task EnsureTableAsync(
        CancellationToken cancellationToken)
    {
        await _table.CreateIfNotExistsAsync(
            cancellationToken);
    }

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
}