using NotificationService.Domain.Enums;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Domain entity representing a notification and its delivery status.
/// </summary>
public sealed class Notification
{
    public Guid NotificationId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public string Recipient { get; private set; }

    public string? Subject { get; private set; }

    public string Text { get; private set; }

    public string? BlobName { get; private set; }

    public string? FileName { get; private set; }

    public string? ContentType { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public NotificationStatus Status { get; private set; }

    private Notification()
    {
        Recipient = string.Empty;
        Text = string.Empty;
    }

    public Notification(
        Guid notificationId,
        NotificationChannel channel,
        string recipient,
        string text,
        string? subject = null,
        string? blobName = null,
        string? fileName = null,
        string? contentType = null)
    {
        if (notificationId == Guid.Empty)
            throw new ArgumentException(
                "Notification ID cannot be empty.",
                nameof(notificationId));

        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException(
                "Recipient is required.",
                nameof(recipient));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException(
                "Notification text is required.",
                nameof(text));

        NotificationId = notificationId;
        Channel = channel;
        Recipient = recipient;
        Text = text;
        Subject = subject;
        BlobName = blobName;
        FileName = fileName;
        ContentType = contentType;
        CreatedAtUtc = DateTime.UtcNow;
        Status = NotificationStatus.Accepted;
    }

    /// <summary>
    /// Marks the notification as queued for delivery.
    /// </summary>
    public void MarkQueued()
    {
        Status = NotificationStatus.Queued;
    }

    /// <summary>
    /// Marks the notification as being processed.
    /// </summary>
    public void MarkProcessing()
    {
        Status = NotificationStatus.Processing;
    }

    /// <summary>
    /// Marks the notification as sent.
    /// </summary>
    public void MarkSent()
    {
        Status = NotificationStatus.Sent;
    }

    /// <summary>
    /// Marks the notification as waiting to be retried.
    /// </summary>
    public void MarkRetrying()
    {
        Status = NotificationStatus.Retrying;
    }

    /// <summary>
    /// Marks the notification as failed.
    /// </summary>
    public void MarkFailed()
    {
        Status = NotificationStatus.Failed;
    }
}