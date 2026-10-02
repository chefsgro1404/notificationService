using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Health;

/// <summary>
/// Runs the registered health checks at most once per HealthCheck:CacheSeconds and shares the result,
/// so any number of probes (App Service, availability tests, the web app) costs one round of dependency
/// reads per cache period. Concurrent callers wait for the same run instead of starting their own.
/// </summary>
public sealed class HealthReportCache
{
    private readonly HealthCheckService _healthChecks;
    private readonly TimeProvider _time;
    private readonly TimeSpan _cacheFor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CachedHealthReport? _last;

    /// <summary>
    /// Creates the cache.
    /// </summary>
    /// <param name="healthChecks">Runs the registered checks.</param>
    /// <param name="options">Health check settings (cache duration).</param>
    /// <param name="time">Clock used to expire the cached result.</param>
    public HealthReportCache(
        HealthCheckService healthChecks,
        IOptions<HealthCheckOptions> options,
        TimeProvider time)
    {
        _healthChecks = healthChecks;
        _time = time;
        _cacheFor = TimeSpan.FromSeconds(Math.Max(0, options.Value.CacheSeconds));
    }

    /// <summary>
    /// Returns the cached report while it is fresh, otherwise runs the checks and caches the new report.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The report, when it was produced, and whether it came from the cache.</returns>
    public async Task<CachedHealthReport> GetAsync(CancellationToken cancellationToken)
    {
        if (TryGetFresh(out var cached))
        {
            return cached with { FromCache = true };
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (TryGetFresh(out cached))
            {
                return cached with { FromCache = true };
            }

            var report = await _healthChecks.CheckHealthAsync(cancellationToken);

            _last = new CachedHealthReport(report, _time.GetUtcNow(), FromCache: false);

            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetFresh(out CachedHealthReport report)
    {
        report = _last!;

        return _last is not null && _time.GetUtcNow() - _last.CheckedAtUtc < _cacheFor;
    }
}

/// <summary>
/// A health report with the time it was produced.
/// </summary>
/// <param name="Report">The aggregated result of all checks.</param>
/// <param name="CheckedAtUtc">When the checks ran.</param>
/// <param name="FromCache">True when this report was reused instead of running the checks again.</param>
public sealed record CachedHealthReport(HealthReport Report, DateTimeOffset CheckedAtUtc, bool FromCache);
