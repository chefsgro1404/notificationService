using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.DependencyInjection;
using NotificationService.Infrastructure.Speech;

namespace NotificationService.Infrastructure.Tests;

public class AzureSpeechSynthesizerTests
{
    private static readonly byte[] Wav = [0x52, 0x49, 0x46, 0x46, 0x24, 0, 0, 0, 0x57, 0x41, 0x56, 0x45];

    private static readonly SpeechOptions Configured = new()
    {
        Key = "test-key",
        Region = "SouthEastAsia",
        VoiceName = "en-GB-SoniaNeural"
    };

    private static AzureSpeechSynthesizer Create(
        StubHandler handler,
        SpeechOptions? options = null) =>
        new(
            new HttpClient(handler),
            Options.Create(options ?? Configured),
            NullLogger<AzureSpeechSynthesizer>.Instance);

    [Theory]
    [InlineData("", "southeastasia")]
    [InlineData("key", " ")]
    public async Task SynthesizeWavAsync_Throws_WhenNotConfigured(string key, string region)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(handler, new SpeechOptions { Key = key, Region = region })
                .SynthesizeWavAsync("Hello", CancellationToken.None));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task SynthesizeWavAsync_PostsSsmlToRegionalEndpoint_AndReturnsWav()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Wav)
        });

        var audio = await Create(handler).SynthesizeWavAsync("Fish & <chips>", CancellationToken.None);

        Assert.Equal(Wav, audio);

        var request = handler.Request!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://southeastasia.tts.speech.microsoft.com/cognitiveservices/v1", request.RequestUri!.ToString());
        Assert.Equal("test-key", request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single());
        Assert.Equal("riff-24khz-16bit-mono-pcm", request.Headers.GetValues("X-Microsoft-OutputFormat").Single());
        Assert.Contains("NotificationService-TextToWav/1.0", request.Headers.UserAgent.ToString());
        Assert.Equal("application/ssml+xml", handler.ContentType);

        var ssml = XDocument.Parse(handler.Body!);
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        Assert.Equal("en-GB", ssml.Root!.Attribute(XNamespace.Xml + "lang")!.Value);
        var voice = ssml.Root.Element(ns + "voice")!;
        Assert.Equal("en-GB-SoniaNeural", voice.Attribute("name")!.Value);
        Assert.Equal("Fish & <chips>", voice.Value);
    }

    [Fact]
    public async Task SynthesizeWavAsync_Throws_WhenServiceReturnsError()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(new string('x', 600))
        });

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(() =>
            Create(handler).SynthesizeWavAsync("Hello", CancellationToken.None));

        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task SynthesizeWavAsync_Throws_WhenServiceReturnsShortErrorBody()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("slow down")
        });

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(() =>
            Create(handler).SynthesizeWavAsync("Hello", CancellationToken.None));

        Assert.Contains("429", ex.Message);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 0x04 })]
    public async Task SynthesizeWavAsync_Throws_WhenResponseIsNotWav(byte[] body)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });

        await Assert.ThrowsAsync<SpeechSynthesisException>(() =>
            Create(handler).SynthesizeWavAsync("Hello", CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeWavAsync_WrapsNetworkErrors()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("dns"));

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(() =>
            Create(handler).SynthesizeWavAsync("Hello", CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task SynthesizeWavAsync_WrapsTimeouts()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(() =>
            Create(handler).SynthesizeWavAsync("Hello", CancellationToken.None));

        Assert.IsType<TaskCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task SynthesizeWavAsync_PropagatesCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new StubHandler(_ => throw new TaskCanceledException("cancelled"));

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            Create(handler).SynthesizeWavAsync("Hello", cts.Token));
    }

    [Fact]
    public void BuildSsml_EscapesTextAndUsesVoiceLocale()
    {
        var ssml = AzureSpeechSynthesizer.BuildSsml("'quoted' \"text\"", "fr-FR-DeniseNeural");

        Assert.Contains("xml:lang='fr-FR'", ssml);
        Assert.Contains("&apos;quoted&apos; &quot;text&quot;", ssml);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            ContentType = request.Content?.Headers.ContentType?.ToString();
            return respond(request);
        }
    }
}

public class SpeechOptionsTests
{
    [Fact]
    public void Defaults_AreApplied()
    {
        var options = new SpeechOptions();

        Assert.Equal("Speech", SpeechOptions.SectionName);
        Assert.Equal(string.Empty, options.Key);
        Assert.Equal(string.Empty, options.Region);
        Assert.Equal("en-US-JennyNeural", options.VoiceName);
        Assert.Equal(30, options.TimeoutSeconds);
    }

    [Fact]
    public void SynthesisEndpoint_UsesNormalizedRegion()
    {
        var options = new SpeechOptions { Region = " SouthEastAsia " };

        Assert.Equal(
            new Uri("https://southeastasia.tts.speech.microsoft.com/cognitiveservices/v1"),
            options.SynthesisEndpoint);
    }
}

public class SpeechRegistrationTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> extra)
    {
        var values = new Dictionary<string, string?>
        {
            ["ServiceBus:ConnectionString"] =
                "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=key;UseDevelopmentEmulator=true;",
            ["Email:Provider"] = "Smtp",
            ["Voice:Provider"] = "Mock"
        };

        foreach (var (key, value) in extra)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddInfrastructureServices_RegistersSpeechSynthesizer_WithConfiguredTimeout()
    {
        using var provider = BuildProvider(new()
        {
            ["Speech:Key"] = "k",
            ["Speech:Region"] = "southeastasia",
            ["Speech:TimeoutSeconds"] = "12"
        });

        Assert.IsType<AzureSpeechSynthesizer>(provider.GetRequiredService<ISpeechSynthesizer>());
        Assert.Equal("k", provider.GetRequiredService<IOptions<SpeechOptions>>().Value.Key);

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ISpeechSynthesizer));
        Assert.Equal(TimeSpan.FromSeconds(12), client.Timeout);
    }

    [Fact]
    public void AddInfrastructureServices_DefaultsSpeechTimeoutTo30Seconds()
    {
        using var provider = BuildProvider(new());

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ISpeechSynthesizer));

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
    }
}
