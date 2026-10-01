using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NotificationService.Application.Interfaces;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.DependencyInjection;
using NotificationService.Infrastructure.Storage;

namespace NotificationService.Infrastructure.Tests;

public class AzureTableAudioCategoryStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<TableClient> _table = new();

    private AzureTableAudioCategoryStore CreateStore() => new(_table.Object, new FixedTimeProvider(Now));

    private static AudioCategoryEntity Row(int id, string name, int? audioId = null) => new()
    {
        RowKey = AudioCategoryEntity.ToRowKey(id),
        CategoryId = id,
        Name = name,
        AudioId = audioId,
        CreatedAtUtc = Now.AddDays(-id),
        UpdatedAtUtc = Now.AddDays(-id)
    };

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenConnectionStringIsMissing(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new AzureTableAudioCategoryStore(
                Options.Create(new AuditStorageOptions { ConnectionString = connectionString }),
                TimeProvider.System));
    }

    [Fact]
    public void Constructor_CreatesTableClient_WhenConfigured()
    {
        Assert.NotNull(new AzureTableAudioCategoryStore(
            Options.Create(new AuditStorageOptions { ConnectionString = "UseDevelopmentStorage=true" }),
            TimeProvider.System));
        Assert.Equal("AudioCategory", AzureTableAudioCategoryStore.TableName);
    }

    [Fact]
    public void ToRowKey_PadsIds()
    {
        Assert.Equal("0000000012", AudioCategoryEntity.ToRowKey(12));
    }

    [Fact]
    public async Task CreateAsync_IssuesNextIdAndStoresTheCategory()
    {
        _table.SetupGet(AzureTableAudioCategoryStore.CounterRowKey,
            new IdCounterEntity { RowKey = "category-id", LastId = 2, ETag = new ETag("\"v\"") });
        AudioCategoryEntity? added = null;
        _table
            .Setup(x => x.AddEntityAsync(It.IsAny<AudioCategoryEntity>(), It.IsAny<CancellationToken>()))
            .Callback<AudioCategoryEntity, CancellationToken>((e, _) => added = e)
            .ReturnsAsync(Mock.Of<Response>());
        _table.SetupQuery(Array.Empty<AudioCategoryEntity>());

        var store = CreateStore();
        var created = await store.CreateAsync("Support", CancellationToken.None);
        await store.ListAsync(CancellationToken.None);

        Assert.Equal((3, "Support", (int?)null, Now, Now), (created.Id, created.Name, created.AudioId, created.CreatedAtUtc, created.UpdatedAtUtc));
        Assert.Equal(("category", "0000000003", 3), (added!.PartitionKey, added.RowKey, added.CategoryId));
        _table.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_ReadsTheCategoryPartition_OrderedById()
    {
        string? filter = null;
        _table.SetupQuery(new[] { Row(3, "C", 9), Row(1, "A") }, f => filter = f);

        var list = await CreateStore().ListAsync(CancellationToken.None);

        Assert.Equal("PartitionKey eq 'category'", filter);
        Assert.Equal([1, 3], list.Select(x => x.Id));
        Assert.Equal(9, list[1].AudioId);
    }

    [Fact]
    public async Task GetAsync_ReturnsTheCategory_OrNull()
    {
        _table.SetupGet(AudioCategoryEntity.ToRowKey(2), Row(2, "Support", 5));
        _table.SetupGet<AudioCategoryEntity>(AudioCategoryEntity.ToRowKey(4), null);

        var found = await CreateStore().GetAsync(2, CancellationToken.None);

        Assert.Equal((2, "Support", (int?)5), (found!.Id, found.Name, found.AudioId));
        Assert.Null(await CreateStore().GetAsync(4, CancellationToken.None));
    }

    [Fact]
    public async Task LinkAudioAsync_SavesTheAudioIdAndTime()
    {
        _table.SetupGet(AudioCategoryEntity.ToRowKey(2), Row(2, "Support", 5));

        var linked = await CreateStore().LinkAudioAsync(2, 8, CancellationToken.None);

        Assert.Equal((8, Now), (linked!.AudioId, linked.UpdatedAtUtc));
        _table.Verify(x => x.UpdateEntityAsync(
                It.Is<AudioCategoryEntity>(e => e.AudioId == 8 && e.UpdatedAtUtc == Now),
                ETag.All,
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LinkAudioAsync_ReturnsNull_WhenCategoryIsMissing()
    {
        _table.SetupGet<AudioCategoryEntity>(AudioCategoryEntity.ToRowKey(4), null);

        Assert.Null(await CreateStore().LinkAudioAsync(4, 8, CancellationToken.None));
    }

    [Fact]
    public void AddInfrastructureServices_RegistersAudioCategoryStore()
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

        Assert.IsType<AzureTableAudioCategoryStore>(provider.GetRequiredService<IAudioCategoryStore>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
