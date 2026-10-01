using Microsoft.Extensions.DependencyInjection;
using Moq;
using NotificationService.Application.DependencyInjection;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Application.Services;
using NotificationService.Application.Validation;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Tests;

public class SpeechTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  Hello  ", "Hello")]
    [InlineData("Line1\r\nLine2\tEnd", "Line1  Line2 End")]
    [InlineData("Bell\u0007Char", "Bell Char")]
    [InlineData("Hi 👋", "Hi 👋")]
    public void Normalize_TrimsAndReplacesControlCharacters(string? input, string expected)
    {
        Assert.Equal(expected, SpeechText.Normalize(input));
    }

    [Fact]
    public void CountCharacters_CountsCodePoints()
    {
        Assert.Equal(4, SpeechText.CountCharacters("Hi 👋"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenEmpty()
    {
        Assert.Equal("Text is required.", SpeechText.Validate(string.Empty));
    }

    [Fact]
    public void Validate_AcceptsTextAtTheLimit_CountingEmojiAsOneCharacter()
    {
        Assert.Null(SpeechText.Validate(new string('a', SpeechText.MaxLength)));
        Assert.Null(SpeechText.Validate(string.Concat(Enumerable.Repeat("👋", SpeechText.MaxLength))));
    }

    [Fact]
    public void Validate_ReturnsError_WhenTooLong()
    {
        var error = SpeechText.Validate(new string('a', SpeechText.MaxLength + 1));

        Assert.Equal("Text must be 150 characters or fewer (received 151).", error);
    }
}

public class TextToSpeechServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ReadUri = new("https://acct.blob.core.windows.net/notifications/T2A/a.wav?sp=r&sig=x");
    private static readonly byte[] Wav = [0x52, 0x49, 0x46, 0x46, 1, 2, 3];

    private readonly Mock<ISpeechSynthesizer> _synthesizer = new();
    private readonly Mock<IBlobStorage> _blobStorage = new();
    private readonly Mock<ITextToSpeechLogStore> _logStore = new();
    private readonly List<TextToSpeechLogItem> _added = [];
    private readonly List<TextToSpeechLogItem> _updated = [];

    public TextToSpeechServiceTests()
    {
        _blobStorage
            .Setup(x => x.GetReadUri(It.IsAny<string>(), TimeSpan.FromMinutes(60)))
            .Returns(ReadUri);

        _logStore
            .Setup(x => x.NextIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        _logStore
            .Setup(x => x.AddAsync(It.IsAny<TextToSpeechLogItem>(), It.IsAny<CancellationToken>()))
            .Callback<TextToSpeechLogItem, CancellationToken>((item, _) => _added.Add(item))
            .Returns(Task.CompletedTask);

        _logStore
            .Setup(x => x.UpdateAsync(It.IsAny<TextToSpeechLogItem>(), It.IsAny<CancellationToken>()))
            .Callback<TextToSpeechLogItem, CancellationToken>((item, _) => _updated.Add(item))
            .Returns(Task.CompletedTask);
    }

    private TextToSpeechService CreateService() =>
        new(_synthesizer.Object, _blobStorage.Object, _logStore.Object, new FixedTimeProvider(Now));

    private static TextToSpeechLogItem Item(int id, AudioStatus status, bool hasAudio = true) => new()
    {
        Id = id,
        NotificationId = Guid.NewGuid(),
        Text = "Hello",
        BlobName = hasAudio ? $"T2A/{id}.wav" : string.Empty,
        ContentType = "audio/wav",
        SizeBytes = hasAudio ? 10 : 0,
        CharacterCount = 5,
        Status = status,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
        AudioUrl = new Uri("https://stale.example/old.wav"),
        AudioUrlExpiresAtUtc = Now
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateAsync_Throws_WhenTextIsInvalid(string text)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().GenerateAsync(text, CancellationToken.None));

        _synthesizer.VerifyNoOtherCalls();
        _logStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateAsync_Throws_WhenTextIsTooLong()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().GenerateAsync(new string('a', 151), CancellationToken.None));
    }

    [Fact]
    public void BlobNameFor_UsesT2AFolderAndNotificationId()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal("T2A/11111111-2222-3333-4444-555555555555.wav", TextToSpeechService.BlobNameFor(id));
    }

    [Fact]
    public async Task GenerateAsync_LogsPendingRecord_ThenStoresAudio_ThenMarksItActive()
    {
        string? uploadedBlob = null;
        byte[]? uploadedBytes = null;

        _synthesizer
            .Setup(x => x.SynthesizeWavAsync("Hello world", It.IsAny<CancellationToken>()))
            .Callback(() => Assert.Single(_added))
            .ReturnsAsync(Wav);

        _blobStorage
            .Setup(x => x.UploadBlobAsync(It.IsAny<Stream>(), It.IsAny<string>(), "audio/wav", It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((stream, name, _, _) =>
            {
                uploadedBlob = name;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                uploadedBytes = copy.ToArray();
            })
            .Returns(Task.CompletedTask);

        var result = await CreateService().GenerateAsync("  Hello world ", CancellationToken.None);

        var pending = Assert.Single(_added);
        Assert.Equal(42, pending.Id);
        Assert.NotEqual(Guid.Empty, pending.NotificationId);
        Assert.Equal(AudioStatus.AudioNotGenerated, pending.Status);
        Assert.Equal(string.Empty, pending.BlobName);
        Assert.Equal(0, pending.SizeBytes);
        Assert.Equal(("Hello world", 11), (pending.Text, pending.CharacterCount));
        Assert.Equal((Now, Now), (pending.CreatedAtUtc, pending.UpdatedAtUtc));

        var expectedBlob = $"T2A/{pending.NotificationId}.wav";
        Assert.Equal(expectedBlob, uploadedBlob);
        Assert.Equal(Wav, uploadedBytes);

        var generated = Assert.Single(_updated);
        Assert.Equal((42, pending.NotificationId), (generated.Id, generated.NotificationId));
        Assert.Equal(AudioStatus.Active, generated.Status);
        Assert.Equal(expectedBlob, generated.BlobName);
        Assert.Equal(Wav.Length, generated.SizeBytes);
        Assert.Null(generated.ErrorMessage);
        Assert.Null(generated.AudioUrl);

        Assert.Equal(42, result.Id);
        Assert.Equal(pending.NotificationId, result.NotificationId);
        Assert.Equal(expectedBlob, result.BlobName);
        Assert.Equal(ReadUri, result.AudioUrl);
        Assert.Equal(Now.AddMinutes(60), result.ExpiresAtUtc);
        Assert.Equal(("audio/wav", (long)Wav.Length, 11), (result.ContentType, result.SizeBytes, result.CharacterCount));
    }

    [Fact]
    public async Task GenerateAsync_KeepsRecordNotGenerated_WithSpeechError_WhenSynthesisFails()
    {
        _synthesizer
            .Setup(x => x.SynthesizeWavAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SpeechSynthesisException("Azure AI Speech returned 401 (Unauthorized)."));

        await Assert.ThrowsAsync<SpeechSynthesisException>(() =>
            CreateService().GenerateAsync("Hello", CancellationToken.None));

        var failed = Assert.Single(_updated);
        Assert.Equal(AudioStatus.AudioNotGenerated, failed.Status);
        Assert.Equal("Azure AI Speech returned 401 (Unauthorized).", failed.ErrorMessage);
        Assert.Equal(string.Empty, failed.BlobName);
        _blobStorage.Verify(x => x.UploadBlobAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_RecordsGenericError_WhenUploadFails()
    {
        _synthesizer
            .Setup(x => x.SynthesizeWavAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Wav);

        _blobStorage
            .Setup(x => x.UploadBlobAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage key: secret"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().GenerateAsync("Hello", CancellationToken.None));

        var failed = Assert.Single(_updated);
        Assert.Equal(AudioStatus.AudioNotGenerated, failed.Status);
        Assert.Equal("Audio generation failed unexpectedly.", failed.ErrorMessage);
    }

    [Fact]
    public async Task GenerateAsync_RethrowsOriginalError_WhenRecordingTheFailureAlsoFails()
    {
        _synthesizer
            .Setup(x => x.SynthesizeWavAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SpeechSynthesisException("down"));

        _logStore
            .Setup(x => x.UpdateAsync(It.IsAny<TextToSpeechLogItem>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("table down"));

        await Assert.ThrowsAsync<SpeechSynthesisException>(() =>
            CreateService().GenerateAsync("Hello", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_DoesNotRecordAnError_WhenCancelled()
    {
        _synthesizer
            .Setup(x => x.SynthesizeWavAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateService().GenerateAsync("Hello", CancellationToken.None));

        Assert.Single(_added);
        Assert.Empty(_updated);
    }

    [Fact]
    public async Task GetLogsAsync_AddsUrlsOnlyToActiveGeneratedAudio()
    {
        var query = new TextToSpeechLogQuery();

        _logStore
            .Setup(x => x.QueryAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<TextToSpeechLogItem>
            {
                Items =
                [
                    Item(3, AudioStatus.Active),
                    Item(2, AudioStatus.Inactive),
                    Item(1, AudioStatus.AudioNotGenerated, hasAudio: false),
                    Item(0, AudioStatus.Active, hasAudio: false)
                ],
                Page = 1,
                PageSize = 20,
                TotalCount = 4,
                Truncated = true
            });

        var page = await CreateService().GetLogsAsync(query, CancellationToken.None);

        Assert.Equal((1, 20, 4, true), (page.Page, page.PageSize, page.TotalCount, page.Truncated));
        Assert.Equal(ReadUri, page.Items[0].AudioUrl);
        Assert.Equal(Now.AddMinutes(60), page.Items[0].AudioUrlExpiresAtUtc);
        Assert.All(page.Items.Skip(1), x => Assert.Null(x.AudioUrl));
        Assert.All(page.Items.Skip(1), x => Assert.Null(x.AudioUrlExpiresAtUtc));
    }

    [Fact]
    public async Task GetLogAsync_ReturnsItemWithUrl_OrNull()
    {
        _logStore.Setup(x => x.GetAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(Item(3, AudioStatus.Active));

        var found = await CreateService().GetLogAsync(3, CancellationToken.None);
        var missing = await CreateService().GetLogAsync(4, CancellationToken.None);

        Assert.Equal(ReadUri, found!.AudioUrl);
        Assert.Null(missing);
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesGeneratedAudio()
    {
        _logStore.Setup(x => x.GetAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(Item(5, AudioStatus.Active));
        _logStore
            .Setup(x => x.UpdateStatusAsync(5, AudioStatus.Inactive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Item(5, AudioStatus.Inactive));

        var updated = await CreateService().UpdateStatusAsync(5, AudioStatus.Inactive, CancellationToken.None);

        Assert.Equal(AudioStatus.Inactive, updated!.Status);
        Assert.Null(updated.AudioUrl);
    }

    [Fact]
    public async Task UpdateStatusAsync_AllowsDeletingAudioThatWasNeverGenerated()
    {
        _logStore.Setup(x => x.GetAsync(6, It.IsAny<CancellationToken>())).ReturnsAsync(Item(6, AudioStatus.AudioNotGenerated, hasAudio: false));
        _logStore
            .Setup(x => x.UpdateStatusAsync(6, AudioStatus.Deleted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Item(6, AudioStatus.Deleted, hasAudio: false));

        var updated = await CreateService().UpdateStatusAsync(6, AudioStatus.Deleted, CancellationToken.None);

        Assert.Equal(AudioStatus.Deleted, updated!.Status);
    }

    [Theory]
    [InlineData(AudioStatus.Active)]
    [InlineData(AudioStatus.Inactive)]
    public async Task UpdateStatusAsync_Throws_WhenActivatingAudioThatWasNeverGenerated(AudioStatus status)
    {
        _logStore.Setup(x => x.GetAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Item(7, AudioStatus.Deleted, hasAudio: false));

        var ex = await Assert.ThrowsAsync<AudioStatusChangeException>(() =>
            CreateService().UpdateStatusAsync(7, status, CancellationToken.None));

        Assert.Equal("Audio 7 was never generated, so it can only be deleted.", ex.Message);
        _logStore.Verify(x => x.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<AudioStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateStatusAsync_Throws_WhenSettingAudioNotGenerated()
    {
        await Assert.ThrowsAsync<AudioStatusChangeException>(() =>
            CreateService().UpdateStatusAsync(1, AudioStatus.AudioNotGenerated, CancellationToken.None));

        _logStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateStatusAsync_ReturnsNull_WhenMissing()
    {
        Assert.Null(await CreateService().UpdateStatusAsync(8, AudioStatus.Active, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateStatusAsync_ReturnsNull_WhenRecordDisappearsDuringUpdate()
    {
        _logStore.Setup(x => x.GetAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(Item(9, AudioStatus.Active));

        Assert.Null(await CreateService().UpdateStatusAsync(9, AudioStatus.Deleted, CancellationToken.None));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public class PagedResultTests
{
    [Fact]
    public void Create_ReturnsRequestedPage()
    {
        var page = PagedResult.Create(Enumerable.Range(1, 45).ToList(), page: 3, pageSize: 20, truncated: true);

        Assert.Equal([41, 42, 43, 44, 45], page.Items);
        Assert.Equal(45, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.Truncated);
    }

    [Fact]
    public void Create_ReturnsEmptyPage_PastTheEnd()
    {
        var page = PagedResult.Create(new List<int> { 1 }, page: 5, pageSize: 10);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalPages);
        Assert.False(page.Truncated);
    }

    [Fact]
    public void TotalPages_IsZero_WhenNothingMatched()
    {
        Assert.Equal(0, PagedResult.Create(new List<int>(), 1, 20).TotalPages);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Create_Throws_ForInvalidPaging(int page, int pageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagedResult.Create(new List<int>(), page, pageSize));
    }
}

public class LogQueryDefaultsTests
{
    [Fact]
    public void NotificationLogQuery_Defaults()
    {
        var query = new NotificationLogQuery();

        Assert.Null(query.Channel);
        Assert.Null(query.Status);
        Assert.Null(query.Recipient);
        Assert.Null(query.FromUtc);
        Assert.Null(query.ToUtc);
        Assert.Equal((1, 20), (query.Page, query.PageSize));
    }

    [Fact]
    public void TextToSpeechLogQuery_Defaults_HideOnlyDeletedAudio()
    {
        var query = new TextToSpeechLogQuery();

        Assert.Equal([AudioStatus.AudioNotGenerated, AudioStatus.Active, AudioStatus.Inactive], query.Statuses);
        Assert.Null(query.Search);
        Assert.Equal((1, 20), (query.Page, query.PageSize));
    }

    [Fact]
    public void AudioStatusUpdateRequest_RoundTrips()
    {
        Assert.Null(new AudioStatusUpdateRequest().Status);
        Assert.Equal("Deleted", new AudioStatusUpdateRequest { Status = "Deleted" }.Status);
    }

    [Fact]
    public void AudioStatusChangeException_KeepsMessage()
    {
        Assert.Equal("nope", new AudioStatusChangeException("nope").Message);
    }

    [Fact]
    public void TextToSpeechLogItem_SerializesStatusByName_AndHidesHasAudio()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new TextToSpeechLogItem
        {
            Id = 1,
            NotificationId = Guid.Empty,
            Text = "t",
            BlobName = string.Empty,
            ContentType = "audio/wav",
            SizeBytes = 0,
            CharacterCount = 1,
            Status = AudioStatus.AudioNotGenerated,
            ErrorMessage = "down",
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch
        });

        Assert.Contains("\"Status\":\"AudioNotGenerated\"", json);
        Assert.Contains("\"ErrorMessage\":\"down\"", json);
        Assert.DoesNotContain("HasAudio", json);
    }
}

public class TextToSpeechModelTests
{
    [Fact]
    public void Request_Defaults_ToNullText()
    {
        Assert.Null(new TextToSpeechRequest().Text);
        Assert.Equal("Hi", new TextToSpeechRequest { Text = "Hi" }.Text);
    }

    [Fact]
    public void SpeechSynthesisException_KeepsMessageAndInnerException()
    {
        var inner = new HttpRequestException("boom");

        var ex = new SpeechSynthesisException("failed", inner);

        Assert.Equal("failed", ex.Message);
        Assert.Same(inner, ex.InnerException);
        Assert.Null(new SpeechSynthesisException("failed").InnerException);
    }
}

public class ApplicationServiceRegistrationTests
{
    [Fact]
    public void AddApplicationServices_RegistersTextToSpeechAndTimeProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ISpeechSynthesizer>());
        services.AddSingleton(Mock.Of<IBlobStorage>());
        services.AddSingleton(Mock.Of<ITextToSpeechLogStore>());

        services.AddApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<TextToSpeechService>(scope.ServiceProvider.GetRequiredService<ITextToSpeechService>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }
}
