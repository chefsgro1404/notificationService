using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.DependencyInjection;
using NotificationService.Infrastructure.Storage;

namespace NotificationService.Infrastructure.Tests;

internal static class TableMocks
{
    public static void SetupQuery<T>(
        this Mock<TableClient> table,
        IEnumerable<T> rows,
        Action<string?>? onFilter = null)
        where T : class, ITableEntity
    {
        table
            .Setup(x => x.QueryAsync<T>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, int?, IEnumerable<string>, CancellationToken>((filter, _, _, _) => onFilter?.Invoke(filter))
            .Returns(AsyncPageable<T>.FromPages([Page<T>.FromValues(rows.ToList(), null, Mock.Of<Response>())]));
    }

    public static void SetupGet<T>(
        this Mock<TableClient> table,
        string rowKey,
        T? entity)
        where T : class, ITableEntity
    {
        NullableResponse<T> response;

        if (entity is null)
        {
            var missing = new Mock<NullableResponse<T>>();
            missing.SetupGet(x => x.HasValue).Returns(false);
            response = missing.Object;
        }
        else
        {
            response = Response.FromValue(entity, Mock.Of<Response>());
        }

        table
            .Setup(x => x.GetEntityIfExistsAsync<T>(
                It.IsAny<string>(),
                rowKey,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }
}

public class TableScanTests
{
    [Fact]
    public async Task ReadAsync_ReturnsAllRows_WhenUnderTheLimit()
    {
        var table = new Mock<TableClient>();
        string? filter = null;
        table.SetupQuery(new[] { new TableEntity("p", "1"), new TableEntity("p", "2") }, f => filter = f);

        var (rows, truncated) = await TableScan.ReadAsync<TableEntity>(table.Object, "PartitionKey eq 'p'", 5, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.False(truncated);
        Assert.Equal("PartitionKey eq 'p'", filter);
    }

    [Fact]
    public async Task ReadAsync_StopsAndFlagsTruncation_AtTheLimit()
    {
        var table = new Mock<TableClient>();
        table.SetupQuery(Enumerable.Range(1, 5).Select(x => new TableEntity("p", x.ToString())));

        var (rows, truncated) = await TableScan.ReadAsync<TableEntity>(table.Object, null, 3, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.True(truncated);
    }

    [Fact]
    public async Task ReadAsync_IsNotTruncated_WhenRowsEqualTheLimit()
    {
        var table = new Mock<TableClient>();
        table.SetupQuery(Enumerable.Range(1, 3).Select(x => new TableEntity("p", x.ToString())));

        var (rows, truncated) = await TableScan.ReadAsync<TableEntity>(table.Object, null, 3, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.False(truncated);
    }

    [Fact]
    public void And_CombinesNonEmptyClauses()
    {
        Assert.Null(TableScan.And());
        Assert.Null(TableScan.And(null, ""));
        Assert.Equal("(a)", TableScan.And("a", null));
        Assert.Equal("(a) and (b or c)", TableScan.And("a", "b or c"));
    }
}

public class AzureTableAuditStoreQueryTests
{
    private readonly Mock<TableClient> _table = new();

    private static NotificationAuditEntity Row(string id, string recipient, string status, int minute, string? blob = null) => new()
    {
        PartitionKey = "Email",
        RowKey = id,
        Channel = "Email",
        Recipient = recipient,
        Status = status,
        CreatedAtUtc = new DateTime(2026, 9, 30, 10, minute, 0, DateTimeKind.Unspecified),
        UpdatedAtUtc = new DateTime(2026, 9, 30, 11, minute, 0, DateTimeKind.Utc),
        ProviderMessageId = "pm-" + id,
        ErrorCode = status == "Failed" ? "E1" : null,
        ErrorMessage = status == "Failed" ? "bad" : null,
        RetryCount = 2,
        BlobName = blob
    };

    [Fact]
    public async Task QueryAsync_BuildsServerFilter_FromAllOptions()
    {
        string? filter = null;
        _table.SetupQuery(Array.Empty<NotificationAuditEntity>(), f => filter = f);

        await new AzureTableAuditStore(_table.Object).QueryAsync(
            new NotificationLogQuery
            {
                Channel = NotificationChannel.Email,
                Status = NotificationStatus.Failed,
                FromUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                ToUtc = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero)
            },
            CancellationToken.None);

        Assert.Equal(
            "(PartitionKey eq 'Email') and (Status eq 'Failed') and " +
            "(CreatedAtUtc ge datetime'2026-09-01T00:00:00Z') and " +
            "(CreatedAtUtc lt datetime'2026-09-02T00:00:00Z')",
            filter);

        _table.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryAsync_WithoutFilters_ReadsEverything()
    {
        string? filter = "unset";
        _table.SetupQuery(Array.Empty<NotificationAuditEntity>(), f => filter = f);

        var page = await new AzureTableAuditStore(_table.Object).QueryAsync(new NotificationLogQuery(), CancellationToken.None);

        Assert.Null(filter);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task QueryAsync_FiltersRecipient_SortsNewestFirst_AndPages()
    {
        _table.SetupQuery(new[]
        {
            Row("a", "alice@contoso.com", "Sent", 1),
            Row("b", "bob@fabrikam.com", "Failed", 2),
            Row("c", "ALICE@contoso.com", "Failed", 3, blob: "notifications/c/file.pdf"),
            Row("d", "carol@contoso.com", "Sent", 4)
        });

        var page = await new AzureTableAuditStore(_table.Object).QueryAsync(
            new NotificationLogQuery { Recipient = " contoso ", Page = 1, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(["d", "c"], page.Items.Select(x => x.Id));

        var c = page.Items[1];
        Assert.Equal(("Email", "ALICE@contoso.com", "Failed"), (c.Channel, c.Recipient, c.Status));
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 10, 3, 0, TimeSpan.Zero), c.CreatedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 11, 3, 0, TimeSpan.Zero), c.UpdatedAtUtc);
        Assert.Equal(("pm-c", "E1", "bad", 2), (c.ProviderMessageId, c.ErrorCode, c.ErrorMessage, c.RetryCount));
        Assert.True(c.HasAttachment);
        Assert.False(page.Items[0].HasAttachment);
    }
}

public class AzureTableTextToSpeechLogStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<TableClient> _table = new();

    private AzureTableTextToSpeechLogStore CreateStore() =>
        new(_table.Object, new FixedTimeProvider(Now));

    private static TextToSpeechLogEntity Row(int id, string status, string text = "Hello") => new()
    {
        RowKey = Guid.NewGuid().ToString(),
        Id = id,
        Text = text,
        BlobName = $"T2A/{id}.wav",
        ContentType = "audio/wav",
        SizeBytes = 100,
        CharacterCount = text.Length,
        Status = status,
        ErrorMessage = "boom",
        CreatedAtUtc = Now.AddMinutes(-id),
        UpdatedAtUtc = Now.AddMinutes(-id)
    };

    private static TextToSpeechLogItem Item(int id, Guid notificationId, AudioStatus status) => new()
    {
        Id = id,
        NotificationId = notificationId,
        Text = "Hi",
        BlobName = string.Empty,
        ContentType = "audio/wav",
        SizeBytes = 0,
        CharacterCount = 2,
        Status = status,
        ErrorMessage = "err",
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private static RequestFailedException Conflict(int status) => new(status, "conflict");

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenConnectionStringIsMissing(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new AzureTableTextToSpeechLogStore(
                Options.Create(new AuditStorageOptions { ConnectionString = connectionString }),
                TimeProvider.System));
    }

    [Fact]
    public void Constructor_CreatesTableClient_WhenConfigured()
    {
        var store = new AzureTableTextToSpeechLogStore(
            Options.Create(new AuditStorageOptions { ConnectionString = "UseDevelopmentStorage=true" }),
            TimeProvider.System);

        Assert.NotNull(store);
    }

    [Fact]
    public async Task NextIdAsync_CreatesCounterAtOne_AndCreatesTableOnlyOnce()
    {
        _table.SetupGet<IdCounterEntity>(AzureTableTextToSpeechLogStore.CounterRowKey, null);
        var store = CreateStore();

        Assert.Equal(1, await store.NextIdAsync(CancellationToken.None));
        Assert.Equal(1, await store.NextIdAsync(CancellationToken.None));

        _table.Verify(x => x.AddEntityAsync(
                It.Is<IdCounterEntity>(c => c.LastId == 1 && c.PartitionKey == "counter" && c.RowKey == "audio-id"),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _table.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NextIdAsync_IncrementsExistingCounter_WithItsETag()
    {
        var counter = new IdCounterEntity { RowKey = "audio-id", LastId = 41, ETag = new ETag("\"v1\"") };
        _table.SetupGet(AzureTableTextToSpeechLogStore.CounterRowKey, counter);

        var id = await CreateStore().NextIdAsync(CancellationToken.None);

        Assert.Equal(42, id);
        _table.Verify(x => x.UpdateEntityAsync(
                It.Is<IdCounterEntity>(c => c.LastId == 42),
                new ETag("\"v1\""),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(412)]
    public async Task NextIdAsync_RetriesWhenAnotherWriterWins(int status)
    {
        var calls = 0;

        _table
            .Setup(x => x.GetEntityIfExistsAsync<IdCounterEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Response.FromValue(new IdCounterEntity { RowKey = "audio-id", LastId = 5 }, Mock.Of<Response>()));

        _table
            .Setup(x => x.UpdateEntityAsync(It.IsAny<IdCounterEntity>(), It.IsAny<ETag>(), TableUpdateMode.Replace, It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1
                ? Task.FromException<Response>(Conflict(status))
                : Task.FromResult(Mock.Of<Response>()));

        Assert.Equal(6, await CreateStore().NextIdAsync(CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NextIdAsync_Throws_AfterTooManyConflicts()
    {
        _table.SetupGet<IdCounterEntity>(AzureTableTextToSpeechLogStore.CounterRowKey, null);
        _table
            .Setup(x => x.AddEntityAsync(It.IsAny<IdCounterEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Conflict(409));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateStore().NextIdAsync(CancellationToken.None));

        _table.Verify(x => x.AddEntityAsync(It.IsAny<IdCounterEntity>(), It.IsAny<CancellationToken>()),
            Times.Exactly(TableIdCounter.MaxAttempts));
    }

    [Fact]
    public async Task NextIdAsync_DoesNotSwallowOtherErrors()
    {
        _table.SetupGet<IdCounterEntity>(AzureTableTextToSpeechLogStore.CounterRowKey, null);
        _table
            .Setup(x => x.AddEntityAsync(It.IsAny<IdCounterEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Conflict(500));

        await Assert.ThrowsAsync<RequestFailedException>(() => CreateStore().NextIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_StoresEntityKeyedByNotificationId()
    {
        var notificationId = Guid.NewGuid();
        TextToSpeechLogEntity? added = null;
        _table
            .Setup(x => x.AddEntityAsync(It.IsAny<TextToSpeechLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TextToSpeechLogEntity, CancellationToken>((e, _) => added = e)
            .ReturnsAsync(Mock.Of<Response>());

        await CreateStore().AddAsync(Item(3, notificationId, AudioStatus.AudioNotGenerated), CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(("audio", notificationId.ToString(), 3), (added!.PartitionKey, added.RowKey, added.Id));
        Assert.Equal(("Hi", "", "audio/wav", 0L, 2), (added.Text, added.BlobName, added.ContentType, added.SizeBytes, added.CharacterCount));
        Assert.Equal(("AudioNotGenerated", "err", Now, Now), (added.Status, added.ErrorMessage, added.CreatedAtUtc, added.UpdatedAtUtc));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTheRowForTheNotification()
    {
        var notificationId = Guid.NewGuid();

        await CreateStore().UpdateAsync(
            Item(3, notificationId, AudioStatus.Active) with { BlobName = $"T2A/{notificationId}.wav", SizeBytes = 99, ErrorMessage = null },
            CancellationToken.None);

        _table.Verify(x => x.UpdateEntityAsync(
                It.Is<TextToSpeechLogEntity>(e =>
                    e.RowKey == notificationId.ToString() &&
                    e.Status == "Active" &&
                    e.BlobName == $"T2A/{notificationId}.wav" &&
                    e.SizeBytes == 99 &&
                    e.ErrorMessage == null),
                ETag.All,
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAsync_FindsRowByIntegerId()
    {
        var row = Row(4, "Inactive");
        string? filter = null;
        _table.SetupQuery(new[] { row }, f => filter = f);

        var found = await CreateStore().GetAsync(4, CancellationToken.None);

        Assert.Equal("PartitionKey eq 'audio' and Id eq 4", filter);
        Assert.Equal((4, Guid.Parse(row.RowKey), AudioStatus.Inactive), (found!.Id, found.NotificationId, found.Status));
        Assert.Equal(("T2A/4.wav", 100L, 5, "audio/wav"), (found.BlobName, found.SizeBytes, found.CharacterCount, found.ContentType));
        Assert.Equal((Now.AddMinutes(-4), Now.AddMinutes(-4)), (found.CreatedAtUtc, found.UpdatedAtUtc));
        Assert.Equal("boom", found.ErrorMessage);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenMissing()
    {
        _table.SetupQuery(Array.Empty<TextToSpeechLogEntity>());

        Assert.Null(await CreateStore().GetAsync(5, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_TreatsUnknownStatusAsInactive_AndBadRowKeyAsEmptyId()
    {
        var row = Row(6, "Weird");
        row.RowKey = "not-a-guid";
        _table.SetupQuery(new[] { row });

        var found = (await CreateStore().GetAsync(6, CancellationToken.None))!;

        Assert.Equal(AudioStatus.Inactive, found.Status);
        Assert.Equal(Guid.Empty, found.NotificationId);
    }

    [Fact]
    public async Task UpdateStatusAsync_SavesStatusAndTime()
    {
        var row = Row(7, "Active");
        _table.SetupQuery(new[] { row });

        var updated = await CreateStore().UpdateStatusAsync(7, AudioStatus.Deleted, CancellationToken.None);

        Assert.Equal((AudioStatus.Deleted, Now), (updated!.Status, updated.UpdatedAtUtc));
        _table.Verify(x => x.UpdateEntityAsync(
                It.Is<TextToSpeechLogEntity>(e => e.RowKey == row.RowKey && e.Status == "Deleted" && e.UpdatedAtUtc == Now),
                ETag.All,
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_ReturnsNull_WhenMissing()
    {
        _table.SetupQuery(Array.Empty<TextToSpeechLogEntity>());

        Assert.Null(await CreateStore().UpdateStatusAsync(8, AudioStatus.Active, CancellationToken.None));
        _table.Verify(x => x.UpdateEntityAsync(
                It.IsAny<TextToSpeechLogEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task QueryAsync_FiltersByPartitionAndStatus_SearchesText_AndSortsNewestFirst()
    {
        string? filter = null;
        _table.SetupQuery(
            new[] { Row(1, "Active", "Good morning"), Row(3, "Inactive", "good night"), Row(2, "Active", "Hello") },
            f => filter = f);

        var page = await CreateStore().QueryAsync(
            new TextToSpeechLogQuery
            {
                Statuses = [AudioStatus.Active, AudioStatus.Inactive, AudioStatus.Active],
                Search = " GOOD ",
                PageSize = 10
            },
            CancellationToken.None);

        Assert.Equal("(PartitionKey eq 'audio') and (Status eq 'Active' or Status eq 'Inactive')", filter);
        Assert.Equal([3, 1], page.Items.Select(x => x.Id));
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_WithoutSearch_ReturnsEveryRow()
    {
        _table.SetupQuery(new[] { Row(1, "Deleted"), Row(2, "Deleted") });

        var page = await CreateStore().QueryAsync(
            new TextToSpeechLogQuery { Statuses = [AudioStatus.Deleted] },
            CancellationToken.None);

        Assert.Equal([2, 1], page.Items.Select(x => x.Id));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public class LogRegistrationTests
{
    [Fact]
    public void AddInfrastructureServices_RegistersTextToSpeechLogStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceBus:ConnectionString"] =
                    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=key;UseDevelopmentEmulator=true;",
                ["Email:Provider"] = "Smtp",
                ["Voice:Provider"] = "Mock",
                ["AuditStorage:ConnectionString"] = "UseDevelopmentStorage=true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<AzureTableTextToSpeechLogStore>(provider.GetRequiredService<ITextToSpeechLogStore>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.Equal("TextToSpeechLog", new AuditStorageOptions().TextToSpeechTableName);
    }
}
