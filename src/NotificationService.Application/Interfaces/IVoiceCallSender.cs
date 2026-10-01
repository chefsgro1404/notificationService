namespace NotificationService.Application.Interfaces;

/// <summary>
/// Defines operations for placing outbound voice calls that play a prerecorded audio file.
/// </summary>
public interface IVoiceCallSender
{
    /// <summary>
    /// Starts an outbound call to the specified phone number. The audio file is played
    /// once the recipient answers.
    /// </summary>
    /// <param name="recipient">The E.164 phone number of the recipient.</param>
    /// <param name="notificationId">The unique identifier of the notification that triggered the call.</param>
    /// <param name="audioBlobName">The blob to play, for example "T2A/{notificationId}.wav" (resolved from the audio category).</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task containing the provider call identifier.</returns>
    Task<string> StartCallAsync(
        string recipient,
        Guid notificationId,
        string audioBlobName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Handles call events posted back by the voice provider (for example, call answered or playback completed).
    /// </summary>
    /// <param name="eventsJson">The raw JSON body of the provider callback.</param>
    /// <param name="callbackQuery">The query string of the callback URL, which carries the call context.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleCallEventsAsync(
        string eventsJson,
        string? callbackQuery,
        CancellationToken cancellationToken);
}
