namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Azure AI Speech settings used for text-to-speech ("Speech" section).
/// The key is a secret: supply it through local.settings.json, user secrets, an app setting
/// ("Speech__Key") or a Key Vault reference, never through a committed appsettings file.
/// </summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>Azure AI Speech resource key.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Azure region of the Speech resource, for example "southeastasia".</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Neural voice used for synthesis; its locale prefix (e.g. "en-US") becomes the SSML language.</summary>
    public string VoiceName { get; init; } = "en-US-JennyNeural";

    /// <summary>Maximum time to wait for the Speech service before failing the request.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Text-to-speech REST endpoint for <see cref="Region"/>.</summary>
    public Uri SynthesisEndpoint =>
        new($"https://{Region.Trim().ToLowerInvariant()}.tts.speech.microsoft.com/cognitiveservices/v1");
}
