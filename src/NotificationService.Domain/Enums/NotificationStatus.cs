namespace NotificationService.Domain.Enums;

/// <summary>
/// Lifecycle status of a notification.
/// </summary>
public enum NotificationStatus
{
    Accepted = 1,
    Queued = 2,
    Processing = 3,
    Sent = 4,
    Retrying = 5,
    Failed = 6
}