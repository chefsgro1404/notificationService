namespace NotificationService.Application.Models;

/// <summary>
/// JSON body for changing an audio's status, for example <c>{ "status": "Inactive" }</c>.
/// </summary>
public sealed class AudioStatusUpdateRequest
{
    /// <summary>The new status: "Active", "Inactive" or "Deleted".</summary>
    public string? Status { get; init; }
}
