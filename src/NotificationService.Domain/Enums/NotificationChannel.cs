namespace NotificationService.Domain.Enums;

/// <summary>
/// Delivery channel of a notification; its lower-case name is the Service Bus queue name.
/// </summary>
public enum NotificationChannel
{
    Email = 1,
    Sms = 2,
    Telegram = 3
}