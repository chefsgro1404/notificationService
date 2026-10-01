namespace NotificationService.Application.Exceptions;

/// <summary>
/// Raised when an audio status change is not allowed, for example activating audio that was never
/// generated, or setting a record back to AudioNotGenerated.
/// </summary>
public sealed class AudioStatusChangeException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">A user-facing explanation of why the change is not allowed.</param>
    public AudioStatusChangeException(string message)
        : base(message)
    {
    }
}
