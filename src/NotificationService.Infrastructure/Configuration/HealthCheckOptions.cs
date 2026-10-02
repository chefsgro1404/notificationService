namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Settings for the /api/health endpoint ("HealthCheck" section). Results are cached so frequent probes
/// (App Service health check, availability tests, the web app) do not multiply calls to Azure services.
/// </summary>
public sealed class HealthCheckOptions
{
    public const string SectionName = "HealthCheck";

    /// <summary>Queues checked when <see cref="ServiceBusQueues"/> is not set: the ones this app consumes.</summary>
    public static readonly IReadOnlyList<string> DefaultServiceBusQueues = ["email", "telegram"];

    /// <summary>How long a health result is reused before the dependencies are checked again.</summary>
    public int CacheSeconds { get; init; } = 60;

    /// <summary>Maximum time one dependency check may take before it is reported as unhealthy.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Service Bus queues to check; empty means <see cref="DefaultServiceBusQueues"/>.</summary>
    public string[] ServiceBusQueues { get; init; } = [];

    /// <summary>The queues to check: <see cref="ServiceBusQueues"/>, or the defaults when none are configured.</summary>
    public IReadOnlyList<string> QueuesToCheck =>
        ServiceBusQueues.Length > 0 ? ServiceBusQueues : DefaultServiceBusQueues;
}
