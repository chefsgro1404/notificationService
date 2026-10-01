using Microsoft.Extensions.DependencyInjection;
using Moq;
using NotificationService.Application.DependencyInjection;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Tests;

public class AudioCategoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAudioCategoryStore> _store = new();
    private readonly Mock<ITextToSpeechLogStore> _audioLog = new();

    private AudioCategoryService CreateService() => new(_store.Object, _audioLog.Object);

    private static AudioCategory Category(int id, string name, int? audioId = null) => new()
    {
        Id = id,
        Name = name,
        AudioId = audioId,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private static TextToSpeechLogItem Audio(int id, AudioStatus status, bool hasFile = true) => new()
    {
        Id = id,
        NotificationId = Guid.Parse("6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f"),
        Text = "Hello",
        BlobName = hasFile ? "T2A/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav" : string.Empty,
        ContentType = "audio/wav",
        SizeBytes = 10,
        CharacterCount = 5,
        Status = status,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    [Fact]
    public async Task CreateAsync_TrimsTheName_AndCreatesTheCategory()
    {
        _store.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Category(1, "Sales")]);
        _store.Setup(x => x.CreateAsync("Support", It.IsAny<CancellationToken>())).ReturnsAsync(Category(2, "Support"));

        var created = await CreateService().CreateAsync("  Support ", CancellationToken.None);

        Assert.Equal((2, "Support"), (created.Id, created.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public async Task CreateAsync_Throws_ForEmptyOrLongNames(string? name)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => CreateService().CreateAsync(name, CancellationToken.None));

        Assert.StartsWith("Category name must be 1 to 50 characters.", ex.Message);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_Throws_ForDuplicateNamesIgnoringCase()
    {
        _store.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Category(1, "Support")]);

        var ex = await Assert.ThrowsAsync<AudioCategoryException>(() =>
            CreateService().CreateAsync("SUPPORT", CancellationToken.None));

        Assert.Equal("A category named \"SUPPORT\" already exists.", ex.Message);
        _store.Verify(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_OrdersByName()
    {
        _store.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Category(1, "support"), Category(2, "Billing"), Category(3, "Alerts")]);

        var list = await CreateService().ListAsync(CancellationToken.None);

        Assert.Equal(["Alerts", "Billing", "support"], list.Select(x => x.Name));
    }

    [Fact]
    public async Task LinkAudioAsync_LinksActiveAudio()
    {
        _store.Setup(x => x.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Category(1, "Support"));
        _audioLog.Setup(x => x.GetAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Audio(7, AudioStatus.Active));
        _store.Setup(x => x.LinkAudioAsync(1, 7, It.IsAny<CancellationToken>())).ReturnsAsync(Category(1, "Support", 7));

        var linked = await CreateService().LinkAudioAsync(1, 7, CancellationToken.None);

        Assert.Equal(7, linked!.AudioId);
    }

    [Fact]
    public async Task LinkAudioAsync_ReturnsNull_WhenCategoryIsMissing()
    {
        Assert.Null(await CreateService().LinkAudioAsync(1, 7, CancellationToken.None));
        _audioLog.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAudioAsync_Throws_WhenAudioIsMissing()
    {
        _store.Setup(x => x.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Category(1, "Support"));

        var ex = await Assert.ThrowsAsync<AudioCategoryException>(() =>
            CreateService().LinkAudioAsync(1, 7, CancellationToken.None));

        Assert.Equal("Audio 7 was not found.", ex.Message);
    }

    [Theory]
    [InlineData(AudioStatus.Inactive, true)]
    [InlineData(AudioStatus.Deleted, true)]
    [InlineData(AudioStatus.AudioNotGenerated, false)]
    [InlineData(AudioStatus.Active, false)]
    public async Task LinkAudioAsync_Throws_WhenAudioIsNotActiveAndGenerated(AudioStatus status, bool hasFile)
    {
        _store.Setup(x => x.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Category(1, "Support"));
        _audioLog.Setup(x => x.GetAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Audio(7, status, hasFile));

        var ex = await Assert.ThrowsAsync<AudioCategoryException>(() =>
            CreateService().LinkAudioAsync(1, 7, CancellationToken.None));

        Assert.StartsWith("Only active audio can be linked; audio 7 is", ex.Message);
        _store.Verify(x => x.LinkAudioAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAudioBlobAsync_ReturnsTheLinkedActiveAudio()
    {
        _store.Setup(x => x.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Category(1, "Support", 7));
        _audioLog.Setup(x => x.GetAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Audio(7, AudioStatus.Active));

        Assert.Equal(
            "T2A/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav",
            await CreateService().ResolveAudioBlobAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAudioBlobAsync_Throws_WhenCategoryIsMissing()
    {
        var ex = await Assert.ThrowsAsync<AudioCategoryException>(() =>
            CreateService().ResolveAudioBlobAsync(3, CancellationToken.None));

        Assert.Equal("Category 3 was not found.", ex.Message);
    }

    [Fact]
    public async Task ResolveAudioBlobAsync_Throws_WhenNoAudioIsLinked()
    {
        _store.Setup(x => x.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Category(1, "Support"));

        var ex = await Assert.ThrowsAsync<AudioCategoryException>(() =>
            CreateService().ResolveAudioBlobAsync(1, CancellationToken.None));

        Assert.Equal("Category \"Support\" has no linked audio.", ex.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(AudioStatus.Inactive, true)]
    [InlineData(AudioStatus.Active, false)]
    public async Task ResolveAudioBlobAsync_Throws_WhenLinkedAudioIsGoneOrNotActive(AudioStatus? status, bool hasFile)
    {
        _store.Setup(x => x.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Category(1, "Support", 7));
        _audioLog.Setup(x => x.GetAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status is null ? null : Audio(7, status.Value, hasFile));

        var ex = await Assert.ThrowsAsync<AudioCategoryException>(() =>
            CreateService().ResolveAudioBlobAsync(1, CancellationToken.None));

        Assert.Equal("The audio linked to category \"Support\" (audio 7) is not active.", ex.Message);
    }

    [Fact]
    public void Models_RoundTrip()
    {
        Assert.Null(new CreateAudioCategoryRequest().Name);
        Assert.Equal("x", new CreateAudioCategoryRequest { Name = "x" }.Name);
        Assert.Null(new LinkAudioRequest().AudioId);
        Assert.Equal(3, new LinkAudioRequest { AudioId = 3 }.AudioId);
        Assert.Equal("nope", new AudioCategoryException("nope").Message);
    }

    [Fact]
    public void AddApplicationServices_RegistersAudioCategoryService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAudioCategoryStore>());
        services.AddSingleton(Mock.Of<ITextToSpeechLogStore>());
        services.AddApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<AudioCategoryService>(scope.ServiceProvider.GetRequiredService<IAudioCategoryService>());
    }
}

public class AudioBlobNameTests
{
    [Theory]
    [InlineData("T2A/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav", true)]
    [InlineData("T2A/6F1D3F4E-1C2B-4A5D-9E8F-0A1B2C3D4E5F.wav", true)]
    [InlineData("T2A/not-a-guid.wav", false)]
    [InlineData("T2A/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.mp3", false)]
    [InlineData("Audio/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav", false)]
    [InlineData("T2A/../6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAudioBlobName_AcceptsOnlyTextToAudioFiles(string? blob, bool expected)
    {
        Assert.Equal(expected, TextToSpeechService.IsAudioBlobName(blob));
    }
}
