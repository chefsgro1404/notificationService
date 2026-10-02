using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Health;

/// <summary>
/// Checks that each consumed Service Bus queue exists and is reachable by reading its runtime properties.
/// This is a management read (not billed as a messaging operation): nothing is sent, received or peeked.
/// Dead-lettered messages make the result Degraded so failed deliveries are visible.
/// Needs a connection string with Manage rights (for example RootManageSharedAccessKey).
/// </summary>
public sealed class ServiceBusHealthCheck : IHealthCheck
{
    private readonly Lazy<ServiceBusAdministrationClient> _client;
    private readonly IReadOnlyList<string> _queues;

    /// <summary>
    /// Creates the check from the "ServiceBus" and "HealthCheck" settings.
    /// </summary>
    /// <param name="serviceBusOptions">Service Bus connection settings.</param>
    /// <param name="healthOptions">Health check settings, including the queues to check.</param>
    public ServiceBusHealthCheck(
        IOptions<ServiceBusOptions> serviceBusOptions,
        IOptions<HealthCheckOptions> healthOptions)
        : this(
            () => new ServiceBusAdministrationClient(serviceBusOptions.Value.ConnectionString),
            healthOptions.Value.QueuesToCheck)
    {
    }

    internal ServiceBusHealthCheck(
        Func<ServiceBusAdministrationClient> createClient,
        IReadOnlyList<string> queues)
    {
        _client = new Lazy<ServiceBusAdministrationClient>(createClient);
        _queues = queues;
    }

    /// <summary>
    /// Reads the runtime properties of every configured queue.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">A token used to cancel the check (also fired on timeout).</param>
    /// <returns>Healthy, Degraded when messages are dead-lettered, or Unhealthy when a queue cannot be read.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var failures = new List<string>();
        long deadLettered = 0;

        foreach (var queue in _queues)
        {
            try
            {
                QueueRuntimeProperties properties =
                    await _client.Value.GetQueueRuntimePropertiesAsync(queue, cancellationToken);

                data[queue] = new QueueHealth(
                    properties.ActiveMessageCount,
                    properties.DeadLetterMessageCount);

                deadLettered += properties.DeadLetterMessageCount;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"queue '{queue}': {HealthFailure.Reason(ex)}");
            }
        }

        if (failures.Count > 0)
        {
            return HealthCheckResult.Unhealthy(
                $"Service Bus check failed for {string.Join("; ", failures)}.",
                data: data);
        }

        if (deadLettered > 0)
        {
            return HealthCheckResult.Degraded(
                $"All {_queues.Count} queues are reachable, but {deadLettered} message(s) are dead-lettered.",
                data: data);
        }

        return HealthCheckResult.Healthy(
            $"All {_queues.Count} queues are reachable.",
            data);
    }
}

/// <summary>
/// Message counts of one Service Bus queue, shown in the health response.
/// </summary>
/// <param name="ActiveMessages">Messages waiting to be processed.</param>
/// <param name="DeadLetterMessages">Messages that failed processing and were moved to the dead-letter queue.</param>
public sealed record QueueHealth(long ActiveMessages, long DeadLetterMessages);
