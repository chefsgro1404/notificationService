namespace NotificationService.Application.Models;

/// <summary>
/// JSON body accepted by the text-to-speech endpoint, for example <c>{ "text": "Hello" }</c>.
/// </summary>
public sealed class TextToSpeechRequest
{
    /// <summary>The text to convert to speech (at most 150 characters).</summary>
    public string? Text { get; init; }
}
