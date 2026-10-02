using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.Storage;

namespace NotificationService.Infrastructure.Health;

/// <summary>
/// Checks the storage account(s): one blob container existence read and one table listing query.
/// Each run costs two storage read transactions (fractions of a cent per month with caching).
/// Containers and tables are created on first use, so a missing one is reported but is not a failure.
/// </summary>
public sealed class StorageHealthCheck : IHealthCheck
{
    private readonly Lazy<BlobContainerClient> _container;
    private readonly Lazy<TableServiceClient> _tables;
    private readonly IReadOnlyList<string> _tableNames;

    /// <summary>
    /// Creates the check from the "BlobStorage" and "AuditStorage" settings.
    /// </summary>
    /// <param name="blobOptions">Blob container settings.</param>
    /// <param name="auditOptions">Table storage settings.</param>
    public StorageHealthCheck(
        IOptions<BlobStorageOptions> blobOptions,
        IOptions<AuditStorageOptions> auditOptions)
        : this(
            () => new BlobContainerClient(blobOptions.Value.ConnectionString, blobOptions.Value.ContainerName),
            () => new TableServiceClient(auditOptions.Value.ConnectionString),
            [
                auditOptions.Value.TableName,
                AzureTableTextToSpeechLogStore.TableName,
                AzureTableAudioCategoryStore.TableName
            ])
    {
    }

    internal StorageHealthCheck(
        Func<BlobContainerClient> createContainer,
        Func<TableServiceClient> createTables,
        IReadOnlyList<string> tableNames)
    {
        _container = new Lazy<BlobContainerClient>(createContainer);
        _tables = new Lazy<TableServiceClient>(createTables);
        _tableNames = tableNames;
    }

    /// <summary>
    /// Reads the blob container's existence and lists the app's tables.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">A token used to cancel the check (also fired on timeout).</param>
    /// <returns>Healthy when both blob and table storage answer, otherwise Unhealthy.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var failures = new List<string>();

        try
        {
            bool exists = await _container.Value.ExistsAsync(cancellationToken);

            data["blobContainer"] = exists ? "ready" : "not created yet (created on first upload)";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"blob storage: {HealthFailure.Reason(ex)}");
        }

        try
        {
            var filter = string.Join(" or ", _tableNames.Select(name => $"TableName eq '{name}'"));
            var found = new List<string>();

            await foreach (var page in _tables.Value
                               .QueryAsync(filter, maxPerPage: _tableNames.Count, cancellationToken)
                               .AsPages())
            {
                found.AddRange(page.Values.Select(table => table.Name));
                break;
            }

            data["tables"] = _tableNames.ToDictionary(
                name => name,
                name => found.Contains(name, StringComparer.OrdinalIgnoreCase)
                    ? "ready"
                    : "not created yet (created on first use)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"table storage: {HealthFailure.Reason(ex)}");
        }

        return failures.Count > 0
            ? HealthCheckResult.Unhealthy(
                $"Storage check failed for {string.Join("; ", failures)}.",
                data: data)
            : HealthCheckResult.Healthy(
                "Blob and table storage are reachable.",
                data);
    }
}
