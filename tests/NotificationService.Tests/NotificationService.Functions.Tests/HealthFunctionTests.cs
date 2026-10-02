using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NotificationService.Functions.Functions;
using NotificationService.Functions.Http;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.Health;

namespace NotificationService.Functions.Tests.Functions;

public class HealthFunctionTests
{
    private static HealthFunction Function(HealthStatus storage, HealthStatus acs = HealthStatus.Healthy)
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["serviceBus"] = new(HealthStatus.Healthy, "All 2 queues are reachable.", TimeSpan.FromMilliseconds(12),
                    null, new Dictionary<string, object> { ["email"] = new QueueHealth(1, 0) }),
                ["storage"] = new(storage, "storage", TimeSpan.FromMilliseconds(8), null, null),
                ["acs"] = new(acs, "acs", TimeSpan.FromMilliseconds(30), null, null)
            },
            TimeSpan.FromMilliseconds(31));

        var service = new Mock<HealthCheckService>();
        service
            .Setup(x => x.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var cache = new HealthReportCache(
            service.Object, Options.Create(new HealthCheckOptions()), TimeProvider.System);

        return new HealthFunction(cache, NullLogger<HealthFunction>.Instance);
    }

    private static async Task<(ObjectResult Result, HealthResponse Body, HttpRequest Request)> Run(HealthFunction function)
    {
        var request = new DefaultHttpContext().Request;
        var result = Assert.IsType<ObjectResult>(await function.Run(request, CancellationToken.None));
        return (result, Assert.IsType<HealthResponse>(result.Value), request);
    }

    [Fact]
    public async Task Returns200_WithOneBlockPerDependency_WhenHealthy()
    {
        var (result, body, request) = await Run(Function(HealthStatus.Healthy));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("Healthy", body.Status);
        Assert.False(body.Cached);
        Assert.Equal(31, body.TotalDurationMs);
        Assert.Equal(["serviceBus", "storage", "acs"], body.Checks.Keys);
        Assert.Equal(12, body.Checks["serviceBus"].DurationMs);
        Assert.Equal(new QueueHealth(1, 0), body.Checks["serviceBus"].Data["email"]);
        Assert.Equal("no-store", request.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Returns200_WhenDegraded()
    {
        var (result, body, _) = await Run(Function(HealthStatus.Degraded));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("Degraded", body.Status);
    }

    [Fact]
    public async Task Returns503_WhenAnyBlockIsUnhealthy_AndServesTheCachedReportNext()
    {
        var function = Function(HealthStatus.Healthy, acs: HealthStatus.Unhealthy);

        var (first, firstBody, _) = await Run(function);
        var (second, secondBody, _) = await Run(function);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, first.StatusCode);
        Assert.Equal("Unhealthy", firstBody.Checks["acs"].Status);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, second.StatusCode);
        Assert.True(secondBody.Cached);
    }
}
