using Azure;
using Azure.Communication;
using Azure.Communication.CallAutomation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.Voice;

namespace NotificationService.Infrastructure.Tests;

public class AzureCommunicationVoiceCallSenderTests
{
    private const string FakeConnectionString =
        "endpoint=https://test.communication.azure.com/;accesskey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private const string AudioBlob = "T2A/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav";
    private const string AudioQuery = "audio=T2A%2F6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav";

    private static readonly Uri AudioUri = new("https://storage.example.test/notifications/T2A/6f1d3f4e.wav?sig=x");

    private readonly Mock<CallAutomationClient> _client = new();
    private readonly Mock<CallConnection> _connection = new();
    private readonly Mock<CallMedia> _media = new();
    private readonly Mock<IBlobStorage> _blobStorage = new();
    private readonly List<CreateCallOptions> _createdCalls = [];

    public AzureCommunicationVoiceCallSenderTests()
    {
        _client
            .Setup(x => x.CreateCallAsync(It.IsAny<CreateCallOptions>(), It.IsAny<CancellationToken>()))
            .Callback<CreateCallOptions, CancellationToken>((options, _) => _createdCalls.Add(options))
            .ReturnsAsync(() => Response.FromValue(
                CallAutomationModelFactory.CreateCallResult(
                    null!,
                    CallAutomationModelFactory.CallConnectionProperties(
                        callConnectionId: $"call-{_createdCalls.Count}")),
                Mock.Of<Response>()));

        _client.Setup(x => x.GetCallConnection(It.IsAny<string>())).Returns(_connection.Object);
        _connection.Setup(x => x.GetCallMedia()).Returns(_media.Object);

        _blobStorage
            .Setup(x => x.GetReadUri(AudioBlob, TimeSpan.FromMinutes(60)))
            .Returns(AudioUri);
    }

    private static IOptions<AzureCommunicationServicesOptions> AcsOptions(
        string connectionString = "unused",
        string caller = "+18332431394",
        string callbackBaseUrl = "https://voice.example.test") =>
        Options.Create(new AzureCommunicationServicesOptions
        {
            ConnectionString = connectionString,
            CallerPhoneNumber = caller,
            CallbackBaseUrl = callbackBaseUrl
        });

    private static IOptions<VoiceOptions> VoiceOptions(int maxCallAttempts = 2) =>
        Options.Create(new VoiceOptions { MaxCallAttempts = maxCallAttempts });

    private AzureCommunicationVoiceCallSender CreateSender(int maxCallAttempts = 2) =>
        new(
            _client.Object,
            AcsOptions(),
            VoiceOptions(maxCallAttempts: maxCallAttempts),
            _blobStorage.Object,
            Mock.Of<ILogger<AzureCommunicationVoiceCallSender>>());

    private static string Event(
        string type,
        string extraData = """, "resultInformation": { "code": 200, "subCode": 0, "message": "ok" }""") =>
        $$"""
        [{
          "id": "1",
          "source": "calling/callConnections/call-1",
          "type": "Microsoft.Communication.{{type}}",
          "specversion": "1.0",
          "datacontenttype": "application/json",
          "time": "2026-09-24T10:00:00Z",
          "subject": "calling/callConnections/call-1",
          "data": {
            "callConnectionId": "call-1",
            "serverCallId": "server-1",
            "correlationId": "correlation-1",
            "operationContext": "context-1"{{extraData}}
          }
        }]
        """;

    private static string FailedEvent(string type) =>
        Event(type, """, "resultInformation": { "code": 408, "subCode": 0, "message": "failed" }""");

    [Fact]
    public void PublicConstructor_CreatesClient_WhenConfigured()
    {
        var sender = new AzureCommunicationVoiceCallSender(
            AcsOptions(connectionString: FakeConnectionString),
            VoiceOptions(),
            _blobStorage.Object,
            Mock.Of<ILogger<AzureCommunicationVoiceCallSender>>());

        Assert.NotNull(sender);
    }

    [Fact]
    public void PublicConstructor_Throws_WhenConnectionStringIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new AzureCommunicationVoiceCallSender(
                AcsOptions(connectionString: " "),
                VoiceOptions(),
                _blobStorage.Object,
                Mock.Of<ILogger<AzureCommunicationVoiceCallSender>>()));
    }

    [Theory]
    [InlineData(" ", "https://voice.example.test")]
    [InlineData("+18332431394", "")]
    [InlineData("+18332431394", "http://voice.example.test")]
    [InlineData("+18332431394", "not a url")]
    public void Constructor_Throws_WhenSettingsAreInvalid(string caller, string callbackBaseUrl)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new AzureCommunicationVoiceCallSender(
                _client.Object,
                AcsOptions(caller: caller, callbackBaseUrl: callbackBaseUrl),
                VoiceOptions(),
                _blobStorage.Object,
                Mock.Of<ILogger<AzureCommunicationVoiceCallSender>>()));
    }

    [Fact]
    public async Task StartCallAsync_PutsCallContextAndAudioInCallbackUrl()
    {
        var notificationId = Guid.NewGuid();

        var callId = await CreateSender().StartCallAsync("+18173235812", notificationId, AudioBlob, CancellationToken.None);

        var call = Assert.Single(_createdCalls);
        var callback = call.CallbackUri.ToString();

        Assert.Equal("call-1", callId);
        Assert.StartsWith("https://voice.example.test/api/voice/call-events?", callback);
        Assert.Contains($"notificationId={notificationId}", callback);
        Assert.Contains("recipient=%2B18173235812", callback);
        Assert.Contains("attempt=1", callback);
        Assert.Contains(AudioQuery, call.CallbackUri.AbsoluteUri);
        Assert.Equal(notificationId.ToString(), call.OperationContext);
        Assert.Equal("+18332431394", ((PhoneNumberIdentifier)call.CallInvite.SourceCallerIdNumber).PhoneNumber);
    }

    [Theory]
    [InlineData("Audio/message.mp3")]
    [InlineData("T2A/../secret.wav")]
    [InlineData("T2A/not-a-guid.wav")]
    [InlineData("")]
    public async Task StartCallAsync_Rejects_BlobsThatAreNotTextToAudioFiles(string blob)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateSender().StartCallAsync("+18173235812", Guid.NewGuid(), blob, CancellationToken.None));

        Assert.Empty(_createdCalls);
    }

    [Fact]
    public async Task HandleCallEventsAsync_PlaysTheCallbackAudio_WhenCallConnected()
    {
        PlayToAllOptions? played = null;

        _media
            .Setup(x => x.PlayToAllAsync(It.IsAny<PlayToAllOptions>(), It.IsAny<CancellationToken>()))
            .Callback<PlayToAllOptions, CancellationToken>((o, _) => played = o)
            .ReturnsAsync(Mock.Of<Response<PlayResult>>());

        await CreateSender().HandleCallEventsAsync(Event("CallConnected"), $"?attempt=1&{AudioQuery}", CancellationToken.None);

        _client.Verify(x => x.GetCallConnection("call-1"), Times.Once);
        var source = Assert.IsType<FileSource>(Assert.Single(played!.PlaySources));
        Assert.Equal(AudioUri, source.FileUri);
        Assert.Equal("context-1", played.OperationContext);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("?attempt=1&audio=Audio%2Fmessage.mp3")]
    public async Task HandleCallEventsAsync_HangsUp_WhenCallbackHasNoValidAudio(string? query)
    {
        await CreateSender().HandleCallEventsAsync(Event("CallConnected"), query, CancellationToken.None);

        _media.Verify(x => x.PlayToAllAsync(It.IsAny<PlayToAllOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        _connection.Verify(x => x.HangUpAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallEventsAsync_HangsUp_WhenPlayCompleted()
    {
        await CreateSender().HandleCallEventsAsync(Event("PlayCompleted"), null, CancellationToken.None);

        _connection.Verify(x => x.HangUpAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallEventsAsync_HangsUp_WhenPlayFailed()
    {
        await CreateSender().HandleCallEventsAsync(FailedEvent("PlayFailed"), null, CancellationToken.None);

        _connection.Verify(x => x.HangUpAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallEventsAsync_IgnoresNotFound_WhenCallAlreadyEnded()
    {
        _connection
            .Setup(x => x.HangUpAsync(true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Call not found"));

        await CreateSender().HandleCallEventsAsync(Event("PlayCompleted"), null, CancellationToken.None);

        _connection.Verify(x => x.HangUpAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallEventsAsync_ContinuesAfterError_WhenAnEventFails()
    {
        _connection
            .Setup(x => x.HangUpAsync(true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Server error"));

        var exception = await Record.ExceptionAsync(() =>
            CreateSender().HandleCallEventsAsync(Event("PlayCompleted"), null, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task HandleCallEventsAsync_DoesNothing_ForOtherEvents()
    {
        await CreateSender().HandleCallEventsAsync(Event("CallDisconnected"), null, CancellationToken.None);

        _client.Verify(x => x.GetCallConnection(It.IsAny<string>()), Times.Never);
        Assert.Empty(_createdCalls);
    }

    [Fact]
    public async Task HandleCallEventsAsync_RetriesSameRecipientAndAudio_WhenFirstCallIsNotAnswered()
    {
        var notificationId = Guid.NewGuid();

        await CreateSender().HandleCallEventsAsync(
            FailedEvent("CreateCallFailed"),
            $"?notificationId={notificationId}&recipient=%2B18173235812&attempt=1&{AudioQuery}",
            CancellationToken.None);

        var retry = Assert.Single(_createdCalls);

        var target = Assert.IsType<PhoneNumberIdentifier>(retry.CallInvite.Target);
        Assert.Equal("+18173235812", target.PhoneNumber);
        Assert.Contains("attempt=2", retry.CallbackUri.ToString());
        Assert.Contains(AudioQuery, retry.CallbackUri.AbsoluteUri);
    }

    [Fact]
    public async Task HandleCallEventsAsync_GivesUp_WhenMaxAttemptsReached()
    {
        await CreateSender(maxCallAttempts: 2).HandleCallEventsAsync(
            FailedEvent("CreateCallFailed"),
            $"?notificationId={Guid.NewGuid()}&recipient=%2B18173235812&attempt=2&{AudioQuery}",
            CancellationToken.None);

        Assert.Empty(_createdCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task HandleCallEventsAsync_NeverRetries_WhenMaxAttemptsIsZeroOrOne(int maxCallAttempts)
    {
        await CreateSender(maxCallAttempts).HandleCallEventsAsync(
            FailedEvent("CreateCallFailed"),
            $"?notificationId={Guid.NewGuid()}&recipient=%2B18173235812&attempt=1&{AudioQuery}",
            CancellationToken.None);

        Assert.Empty(_createdCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("?notificationId=not-a-guid&recipient=%2B18173235812&attempt=1&" + AudioQuery)]
    [InlineData("?notificationId=6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f&attempt=1&" + AudioQuery)]
    [InlineData("?notificationId=6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f&recipient=%2B18173235812&attempt=x&" + AudioQuery)]
    [InlineData("?notificationId=6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f&recipient=%2B18173235812&attempt=1")]
    public async Task HandleCallEventsAsync_DoesNotRetry_WhenCallContextIsInvalid(string? query)
    {
        await CreateSender().HandleCallEventsAsync(FailedEvent("CreateCallFailed"), query, CancellationToken.None);

        Assert.Empty(_createdCalls);
    }
}

public class MockVoiceCallSenderTests
{
    private const string AudioBlob = "T2A/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav";

    private readonly Mock<IBlobStorage> _blobStorage = new();

    private MockVoiceCallSender CreateSender() =>
        new(
            Options.Create(new VoiceOptions()),
            _blobStorage.Object,
            Mock.Of<ILogger<MockVoiceCallSender>>());

    [Fact]
    public async Task StartCallAsync_ReadsTheGivenAudioBlob_AndReturnsMockCallId()
    {
        _blobStorage
            .Setup(x => x.OpenReadAsync(AudioBlob, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));

        _blobStorage
            .Setup(x => x.GetReadUri(AudioBlob, TimeSpan.FromMinutes(60)))
            .Returns(new Uri("http://127.0.0.1:10000/devstoreaccount1/notifications/T2A/x.wav?sig=x"));

        var callId = await CreateSender().StartCallAsync("+18173235812", Guid.NewGuid(), AudioBlob, CancellationToken.None);

        Assert.StartsWith("mock-call-", callId);
    }

    [Fact]
    public async Task StartCallAsync_Throws_WhenAudioBlobIsMissing()
    {
        _blobStorage
            .Setup(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Blob not found"));

        await Assert.ThrowsAsync<RequestFailedException>(() =>
            CreateSender().StartCallAsync("+18173235812", Guid.NewGuid(), AudioBlob, CancellationToken.None));
    }

    [Fact]
    public async Task HandleCallEventsAsync_CompletesWithoutAction()
    {
        await CreateSender().HandleCallEventsAsync("[]", "?attempt=1", CancellationToken.None);

        _blobStorage.VerifyNoOtherCalls();
    }
}
