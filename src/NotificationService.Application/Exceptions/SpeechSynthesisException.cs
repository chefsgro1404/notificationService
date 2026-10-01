namespace NotificationService.Application.Exceptions;

/// <summary>
/// Raised when the text-to-speech service cannot produce audio, for example because it
/// rejected the request, was unreachable, timed out, or returned an empty or non-WAV response.
/// </summary>
public sealed class SpeechSynthesisException : Exception
{
    /// <summary>
    /// Creates the exception with a message describing the failure.
    /// </summary>
    /// <param name="message">A description of the failure; it must not contain secrets.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public SpeechSynthesisException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
