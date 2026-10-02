using Microsoft.Extensions.Diagnostics.HealthChecks;
using NotificationService.Infrastructure.Health;

namespace NotificationService.Functions.Http;

/// <summary>
/// JSON body of GET /api/health: the overall status plus one block per dependency.
/// </summary>
/// <param name="Status">"Healthy", "Degraded" or "Unhealthy" (the worst block wins).</param>
/// <param name="CheckedAtUtc">When the dependencies were last checked.</param>
/// <param name="Cached">True when the result was reused from an earlier check.</param>
/// <param name="TotalDurationMs">How long the last real check took.</param>
/// <param name="Checks">One block per dependency, keyed by name ("serviceBus", "storage", "acs").</param>
public sealed record HealthResponse(
    string Status,
    DateTimeOffset CheckedAtUtc,
    bool Cached,
    long TotalDurationMs,
    IReadOnlyDictionary<string, HealthCheckBlock> Checks)
{
    /// <summary>
    /// Builds the response body from a cached health report.
    /// </summary>
    /// <param name="cached">The report and when it was produced.</param>
    /// <returns>The response body.</returns>
    public static HealthResponse From(CachedHealthReport cached) =>
        new(
            cached.Report.Status.ToString(),
            cached.CheckedAtUtc,
            cached.FromCache,
            (long)cached.Report.TotalDuration.TotalMilliseconds,
            cached.Report.Entries.ToDictionary(
                entry => entry.Key,
                entry => HealthCheckBlock.From(entry.Value)));
}

/// <summary>
/// Result of one dependency check in the health response.
/// </summary>
/// <param name="Status">"Healthy", "Degraded" or "Unhealthy".</param>
/// <param name="Description">What was checked, or why it failed (no secrets or raw exception messages).</param>
/// <param name="DurationMs">How long this check took.</param>
/// <param name="Data">Extra details, such as queue message counts.</param>
public sealed record HealthCheckBlock(
    string Status,
    string? Description,
    long DurationMs,
    IReadOnlyDictionary<string, object> Data)
{
    /// <summary>
    /// Builds a block from one health report entry.
    /// </summary>
    /// <param name="entry">The entry produced by a health check.</param>
    /// <returns>The response block.</returns>
    public static HealthCheckBlock From(HealthReportEntry entry) =>
        new(
            entry.Status.ToString(),
            entry.Description,
            (long)entry.Duration.TotalMilliseconds,
            entry.Data);
}
