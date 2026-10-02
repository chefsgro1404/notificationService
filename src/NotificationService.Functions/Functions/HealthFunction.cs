using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NotificationService.Functions.Http;
using NotificationService.Infrastructure.Health;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Health endpoint: GET /api/health reports Service Bus, Storage and ACS status as separate blocks.
/// Anonymous so App Service health check and availability tests can call it without a function key; the body
/// holds no secrets. Results are cached (HealthCheck:CacheSeconds) and every check is read-only, so frequent
/// probes neither send messages, emails or calls nor add meaningful cost.
/// Returns 200 for Healthy and Degraded, 503 for Unhealthy.
/// </summary>
public sealed class HealthFunction
{
    private readonly HealthReportCache _cache;
    private readonly ILogger<HealthFunction> _logger;

    /// <summary>
    /// Creates the function.
    /// </summary>
    /// <param name="cache">Runs or reuses the dependency checks.</param>
    /// <param name="logger">Logger for unhealthy results.</param>
    public HealthFunction(
        HealthReportCache cache,
        ILogger<HealthFunction> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns the health of every dependency.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>200 with the report when Healthy or Degraded, 503 with the report when Unhealthy.</returns>
    [Function("Health")]
    public async Task<IActionResult> Run(
            [HttpTrigger(
                    AuthorizationLevel.Anonymous,
                    "get",
                    Route = "health")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var cached = await _cache.GetAsync(cancellationToken);
        var body = HealthResponse.From(cached);

        if (cached.Report.Status != HealthStatus.Healthy && !cached.FromCache)
        {
            _logger.LogWarning(
                "Health check is {Status}: {Checks}",
                body.Status,
                string.Join(", ", body.Checks
                    .Where(check => check.Value.Status != nameof(HealthStatus.Healthy))
                    .Select(check => $"{check.Key}={check.Value.Status} ({check.Value.Description})")));
        }

        request.HttpContext.Response.Headers.CacheControl = "no-store";

        return new ObjectResult(body)
        {
            StatusCode = cached.Report.Status == HealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK
        };
    }
}
