using System.Net.Http.Headers;
using System.Security;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Speech;

/// <summary>
/// <see cref="ISpeechSynthesizer"/> that calls the Azure AI Speech text-to-speech REST API
/// and requests 24 kHz, 16-bit, mono PCM audio in a RIFF (WAV) container.
/// </summary>
public sealed class AzureSpeechSynthesizer : ISpeechSynthesizer
{
    /// <summary>Output format requested from the Speech service.</summary>
    public const string OutputFormat = "riff-24khz-16bit-mono-pcm";

    private const int MaxLoggedErrorLength = 500;

    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();

    private readonly HttpClient _httpClient;
    private readonly SpeechOptions _settings;
    private readonly ILogger<AzureSpeechSynthesizer> _logger;

    /// <summary>
    /// Creates the synthesizer.
    /// </summary>
    /// <param name="httpClient">HTTP client supplied by the HTTP client factory.</param>
    /// <param name="options">Speech key, region and voice.</param>
    /// <param name="logger">Logger for Speech service failures.</param>
    public AzureSpeechSynthesizer(
        HttpClient httpClient,
        IOptions<SpeechOptions> options,
        ILogger<AzureSpeechSynthesizer> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<byte[]> SynthesizeWavAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.Key) ||
            string.IsNullOrWhiteSpace(_settings.Region))
        {
            throw new InvalidOperationException(
                "Speech:Key and Speech:Region must be configured.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _settings.SynthesisEndpoint);

        request.Headers.Add("Ocp-Apim-Subscription-Key", _settings.Key);
        request.Headers.Add("X-Microsoft-OutputFormat", OutputFormat);
        request.Headers.UserAgent.ParseAdd("NotificationService-TextToWav/1.0");

        request.Content = new StringContent(
            BuildSsml(text, _settings.VoiceName),
            Encoding.UTF8);

        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/ssml+xml");

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(
                request,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new SpeechSynthesisException(
                "Azure AI Speech could not be reached or did not respond in time.",
                ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogError(
                    "Azure AI Speech returned {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    body.Length > MaxLoggedErrorLength ? body[..MaxLoggedErrorLength] : body);

                throw new SpeechSynthesisException(
                    $"Azure AI Speech returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!audio.AsSpan().StartsWith(RiffSignature))
            {
                throw new SpeechSynthesisException(
                    "Azure AI Speech returned an empty or non-WAV response.");
            }

            return audio;
        }
    }

    /// <summary>
    /// Builds the SSML document sent to the Speech service. The text is XML-escaped and the
    /// language is taken from the voice name's locale prefix ("en-US-JennyNeural" → "en-US").
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="voiceName">The neural voice name.</param>
    /// <returns>The SSML document.</returns>
    internal static string BuildSsml(
        string text,
        string voiceName)
    {
        var locale = string.Join('-', voiceName.Split('-').Take(2));

        return
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{SecurityElement.Escape(locale)}'>" +
            $"<voice name='{SecurityElement.Escape(voiceName)}'>{SecurityElement.Escape(text)}</voice>" +
            "</speak>";
    }
}
