namespace NotificationService.Application.Exceptions;

/// <summary>
/// Raised when an audio category operation is not possible, for example a duplicate name, linking
/// audio that is not active, or sending a voice notification whose category has no playable audio.
/// The message is safe to show to users.
/// </summary>
public sealed class AudioCategoryException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">A user-facing explanation of the problem.</param>
    public AudioCategoryException(string message)
        : base(message)
    {
    }
}
