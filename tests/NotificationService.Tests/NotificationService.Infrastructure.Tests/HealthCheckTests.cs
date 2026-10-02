using Azure;
using Azure.Communication.PhoneNumbers;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.DependencyInjection;
using NotificationService.Infrastructure.Health;

namespace NotificationService.Infrastructure.Tests;

internal static class HealthTestData
{
    public static readonly HealthCheckContext Context = new();

    public static Response<T> Ok<T>(T value) => Response.FromValue(value, Mock.Of<Response>());

    public static AsyncPageable<T> Pages<T>(params T[] values) where T : notnull =>
        AsyncPageable<T>.FromPages([Page<T>.FromValues(values, null, Mock.Of<Response>())]);
}

public class HealthFailureTests
{
    [Fact]
    public void Reason_DescribesKnownExceptions_WithoutMessages()
    {
        Assert.Equal("403 AuthorizationFailure",
            HealthFailure.Reason(new RequestFailedException(403, "secret details", "AuthorizationFailure", null)));
        Assert.Equal("HTTP 500", HealthFailure.Reason(new RequestFailedException(500, "secret details")));
        Assert.Equal("MessagingEntityNotFound", HealthFailure.Reason(
            new ServiceBusException("secret", ServiceBusFailureReason.MessagingEntityNotFound)));
        Assert.Equal("Unauthorized", HealthFailure.Reason(new UnauthorizedAccessException("secret")));
        Assert.Equal("InvalidOperationException", HealthFailure.Reason(new InvalidOperationException("secret")));
    }
}

public class HealthCheckOptionsTests
{
    [Fact]
    public void QueuesToCheck_UsesDefaults_WhenNoneConfigured()
    {
        Assert.Equal(["email", "telegram"], new HealthCheckOptions().QueuesToCheck);
        Assert.Equal(["sms"], new HealthCheckOptions { ServiceBusQueues = ["sms"] }.QueuesToCheck);
    }
}

public class ServiceBusHealthCheckTests
{
    private readonly Mock<ServiceBusAdministrationClient> _client = new();

    private void Queue(string name, long active, long deadLetter) =>
        _client
            .Setup(x => x.GetQueueRuntimePropertiesAsync(name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HealthTestData.Ok(ServiceBusModelFactory.QueueRuntimeProperties(
                name, active, 0, deadLetter, 0, 0, active + deadLetter, 0,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));

    private ServiceBusHealthCheck Check() => new(() => _client.Object, ["email", "telegram"]);

    [Fact]
    public async Task Healthy_WhenAllQueuesAreReadable_AndNothingIsDeadLettered()
    {
        Queue("email", 2, 0);
        Queue("telegram", 0, 0);

        var result = await Check().CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(new QueueHealth(2, 0), result.Data["email"]);
    }

    [Fact]
    public async Task Degraded_WhenMessagesAreDeadLettered()
    {
        Queue("email", 0, 3);
        Queue("telegram", 0, 1);

        var result = await Check().CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("4 message(s) are dead-lettered", result.Description);
    }

    [Fact]
    public async Task Unhealthy_WhenAQueueCannotBeRead()
    {
        Queue("email", 0, 0);
        _client
            .Setup(x => x.GetQueueRuntimePropertiesAsync("telegram", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("not found", ServiceBusFailureReason.MessagingEntityNotFound));

        var result = await Check().CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("queue 'telegram': MessagingEntityNotFound", result.Description);
        Assert.True(result.Data.ContainsKey("email"));
    }

    [Fact]
    public async Task Unhealthy_WhenTheConnectionStringIsInvalid()
    {
        var check = new ServiceBusHealthCheck(
            Options.Create(new ServiceBusOptions { ConnectionString = "" }),
            Options.Create(new HealthCheckOptions()));

        var result = await check.CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        _client
            .Setup(x => x.GetQueueRuntimePropertiesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Check().CheckHealthAsync(HealthTestData.Context));
    }
}

public class StorageHealthCheckTests
{
    private readonly Mock<BlobContainerClient> _container = new();
    private readonly Mock<TableServiceClient> _tables = new();
    private string? _filter;

    private StorageHealthCheck Check() =>
        new(() => _container.Object, () => _tables.Object, ["NotificationAudit", "AudioCategory"]);

    private void Tables(params string[] names) =>
        _tables
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, int?, CancellationToken>((filter, _, _) => _filter = filter)
            .Returns(HealthTestData.Pages(names.Select(name => TableModelFactory.TableItem(name)).ToArray()));

    private void Container(bool exists) =>
        _container
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(HealthTestData.Ok(exists));

    [Fact]
    public async Task Healthy_WhenBlobAndTablesAnswer()
    {
        Container(true);
        Tables("NotificationAudit", "AudioCategory");

        var result = await Check().CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("ready", result.Data["blobContainer"]);
        Assert.Equal("TableName eq 'NotificationAudit' or TableName eq 'AudioCategory'", _filter);
        var tables = Assert.IsType<Dictionary<string, string>>(result.Data["tables"]);
        Assert.All(tables.Values, status => Assert.Equal("ready", status));
    }

    [Fact]
    public async Task Healthy_WhenContainerAndTablesAreNotCreatedYet()
    {
        Container(false);
        Tables("NotificationAudit");

        var result = await Check().CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.StartsWith("not created yet", (string)result.Data["blobContainer"]);
        var tables = Assert.IsType<Dictionary<string, string>>(result.Data["tables"]);
        Assert.StartsWith("not created yet", tables["AudioCategory"]);
    }

    [Fact]
    public async Task Unhealthy_WhenBlobOrTableStorageFails()
    {
        _container
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "denied", "AuthorizationFailure", null));
        _tables
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Throws(new RequestFailedException(500, "down"));

        var result = await Check().CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("blob storage: 403 AuthorizationFailure", result.Description);
        Assert.Contains("table storage: HTTP 500", result.Description);
    }

    [Fact]
    public async Task PublicConstructor_ReportsUnhealthy_WhenNotConfigured()
    {
        var check = new StorageHealthCheck(
            Options.Create(new BlobStorageOptions { ConnectionString = "" }),
            Options.Create(new AuditStorageOptions()));

        var result = await check.CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}

public class AcsHealthCheckTests
{
    private readonly Mock<PhoneNumbersClient> _client = new();

    private static AzureCommunicationServicesOptions Acs(
        string connectionString = "endpoint=https://acs.example/;accesskey=a2V5",
        string senderAddress = "DoNotReply@example.com",
        string caller = "+18005550100",
        string callback = "https://func.example") =>
        new()
        {
            ConnectionString = connectionString,
            SenderAddress = senderAddress,
            CallerPhoneNumber = caller,
            CallbackBaseUrl = callback
        };

    private AcsHealthCheck Check(AzureCommunicationServicesOptions acs, bool email = true, bool voice = true) =>
        new(() => _client.Object, acs, email, voice);

    private void Caller(PhoneNumberCapabilityType calling) =>
        _client
            .Setup(x => x.GetPurchasedPhoneNumberAsync("+18005550100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(HealthTestData.Ok(PhoneNumbersModelFactory.PurchasedPhoneNumber(
                "id", "+18005550100", "US", PhoneNumberType.TollFree,
                new PhoneNumberCapabilities(calling, PhoneNumberCapabilityType.None),
                PhoneNumberAssignmentType.Application, DateTimeOffset.UtcNow,
                PhoneNumbersModelFactory.PhoneNumberCost(2, "USD", BillingFrequency.Monthly))));

    [Fact]
    public async Task Healthy_NotInUse_WhenNoProviderUsesAcs()
    {
        var result = await Check(Acs(connectionString: ""), email: false, voice: false)
            .CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.StartsWith("Not in use", result.Description);
        _client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("outbound")]
    [InlineData("inbound+outbound")]
    public async Task Healthy_WhenCallerNumberCanCallOut(string calling)
    {
        Caller(new PhoneNumberCapabilityType(calling));

        var result = await Check(Acs()).CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(calling.ToString(), result.Data["callerNumberCalling"]);
    }

    [Fact]
    public async Task Unhealthy_WhenCallerNumberCannotCallOut()
    {
        Caller(PhoneNumberCapabilityType.Inbound);

        var result = await Check(Acs()).CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("cannot place outbound calls", result.Description);
    }

    [Fact]
    public async Task Unhealthy_WhenTheAccessKeyIsRejected()
    {
        _client
            .Setup(x => x.GetPurchasedPhoneNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(401, "denied", "Unauthorized", null));

        var result = await Check(Acs()).CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("ACS check failed: 401 Unauthorized.", result.Description);
    }

    [Fact]
    public async Task EmailOnly_ListsOnePageOfNumbers()
    {
        _client
            .Setup(x => x.GetPurchasedPhoneNumbersAsync(It.IsAny<CancellationToken>()))
            .Returns(HealthTestData.Pages<PurchasedPhoneNumber>());

        var result = await Check(Acs(caller: "", callback: ""), voice: false)
            .CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        _client.Verify(x => x.GetPurchasedPhoneNumbersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "s@x.com", "+1800", "https://f", "ConnectionString")]
    [InlineData("conn", "", "+1800", "https://f", "SenderAddress")]
    [InlineData("conn", "s@x.com", " ", "https://f", "CallerPhoneNumber")]
    [InlineData("conn", "s@x.com", "+1800", "http://f", "CallbackBaseUrl")]
    [InlineData("conn", "s@x.com", "+1800", "", "CallbackBaseUrl")]
    public async Task Unhealthy_WhenConfigurationIsMissing(
        string connectionString, string sender, string caller, string callback, string expected)
    {
        var result = await Check(Acs(connectionString, sender, caller, callback))
            .CheckHealthAsync(HealthTestData.Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains(expected, result.Description);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublicConstructor_ReadsProviders()
    {
        var check = new AcsHealthCheck(
            Options.Create(Acs()),
            Options.Create(new EmailOptions { Provider = "Smtp" }),
            Options.Create(new VoiceOptions { Provider = "azurecommunicationservices" }));

        var result = await check.CheckHealthAsync(HealthTestData.Context);

        // The fake endpoint cannot be reached, so the check reports unhealthy rather than throwing.
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("other provider", result.Data["email"]);
        Assert.Equal("AzureCommunicationServices", result.Data["voice"]);
    }
}

public class HealthReportCacheTests
{
    private readonly Mock<HealthCheckService> _service = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));

    private static HealthReport Report(HealthStatus status) =>
        new(new Dictionary<string, HealthReportEntry>
        {
            ["storage"] = new(status, null, TimeSpan.Zero, null, null)
        }, TimeSpan.Zero);

    private HealthReportCache Cache(int seconds = 60) =>
        new(_service.Object, Options.Create(new HealthCheckOptions { CacheSeconds = seconds }), _time);

    [Fact]
    public async Task ReusesTheReport_UntilItExpires()
    {
        _service
            .SetupSequence(x => x.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(HealthStatus.Healthy))
            .ReturnsAsync(Report(HealthStatus.Unhealthy));

        var cache = Cache();

        var first = await cache.GetAsync(CancellationToken.None);
        _time.Now = _time.Now.AddSeconds(59);
        var second = await cache.GetAsync(CancellationToken.None);
        _time.Now = _time.Now.AddSeconds(1);
        var third = await cache.GetAsync(CancellationToken.None);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.CheckedAtUtc, second.CheckedAtUtc);
        Assert.False(third.FromCache);
        Assert.Equal(HealthStatus.Unhealthy, third.Report.Status);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneRun()
    {
        var release = new TaskCompletionSource<HealthReport>();
        _service
            .Setup(x => x.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .Returns(release.Task);

        var cache = Cache();
        var calls = Enumerable.Range(0, 5).Select(_ => cache.GetAsync(CancellationToken.None)).ToArray();
        release.SetResult(Report(HealthStatus.Healthy));
        var results = await Task.WhenAll(calls);

        Assert.Single(results, r => !r.FromCache);
        _service.Verify(
            x => x.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ZeroCacheSeconds_ChecksEveryTime()
    {
        _service
            .Setup(x => x.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(HealthStatus.Healthy));

        var cache = Cache(seconds: -5);
        await cache.GetAsync(CancellationToken.None);
        var second = await cache.GetAsync(CancellationToken.None);

        Assert.False(second.FromCache);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public class HealthCheckRegistrationTests
{
    [Fact]
    public void AddInfrastructureServices_RegistersTheThreeChecks_WithTheConfiguredTimeout()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceBus:ConnectionString"] =
                    "Endpoint=sb://localhost;SharedAccessKeyName=k;SharedAccessKey=v;UseDevelopmentEmulator=true;",
                ["Email:Provider"] = "Smtp",
                ["Voice:Provider"] = "Mock",
                ["HealthCheck:TimeoutSeconds"] = "7"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        Assert.Equal(["serviceBus", "storage", "acs"], registrations.Select(r => r.Name));
        Assert.All(registrations, r => Assert.Equal(TimeSpan.FromSeconds(7), r.Timeout));
        Assert.NotNull(provider.GetRequiredService<HealthReportCache>());
        Assert.IsType<AcsHealthCheck>(registrations.Single(r => r.Name == "acs").Factory(provider));
    }
}
