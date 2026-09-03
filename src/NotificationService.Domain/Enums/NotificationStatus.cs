namespace NotificationService.Domain.Enums;

public enum NotificationStatus
{
    Accepted = 1,
    Queued = 2,
    Processing = 3,
    Sent = 4,
    Retrying = 5,
    Failed = 6
}