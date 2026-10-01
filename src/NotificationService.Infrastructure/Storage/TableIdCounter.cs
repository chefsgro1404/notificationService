using Azure;
using Azure.Data.Tables;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Storage;

/// <summary>
/// Issues sequential integer ids from a counter row (<see cref="IdCounterEntity"/>) stored in the same table as
/// the data. The row is updated with optimistic concurrency (ETag), so concurrent writers never get the same id.
/// </summary>
internal static class TableIdCounter
{
    /// <summary>How many times allocation is retried when another writer wins the race.</summary>
    public const int MaxAttempts = 10;

    /// <summary>
    /// Reserves the next id (1 for a new counter).
    /// </summary>
    /// <param name="table">The table that holds the counter row.</param>
    /// <param name="counterRowKey">Row key of the counter, in partition <see cref="IdCounterEntity.Partition"/>.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The new id.</returns>
    /// <exception cref="InvalidOperationException">No id could be reserved after <see cref="MaxAttempts"/> tries.</exception>
    public static async Task<int> NextAsync(
        TableClient table,
        string counterRowKey,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var existing = await table.GetEntityIfExistsAsync<IdCounterEntity>(
                IdCounterEntity.Partition,
                counterRowKey,
                cancellationToken: cancellationToken);

            try
            {
                if (!existing.HasValue)
                {
                    await table.AddEntityAsync(
                        new IdCounterEntity { RowKey = counterRowKey, LastId = 1 },
                        cancellationToken);

                    return 1;
                }

                var counter = existing.Value!;
                counter.LastId++;

                await table.UpdateEntityAsync(
                    counter,
                    counter.ETag,
                    TableUpdateMode.Replace,
                    cancellationToken);

                return counter.LastId;
            }
            catch (RequestFailedException ex)
                when (ex.Status is 409 or 412)
            {
                // Another writer created or advanced the counter first; read it again.
            }
        }

        throw new InvalidOperationException(
            $"Could not reserve an id from counter '{counterRowKey}' after {MaxAttempts} attempts.");
    }
}
