namespace NotificationService.Application.Interfaces;

/// <summary>
/// Converts text into spoken audio using a text-to-speech engine.
/// </summary>
public interface ISpeechSynthesizer
{
    /// <summary>
    /// Synthesizes the text into a WAV (RIFF, PCM) audio file.
    /// </summary>
    /// <param name="text">The text to speak. It must already be validated and normalized.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The complete WAV file, including its RIFF header.</returns>
    /// <exception cref="Exceptions.SpeechSynthesisException">The speech service failed or returned no audio.</exception>
    Task<byte[]> SynthesizeWavAsync(
        string text,
        CancellationToken cancellationToken);
}
