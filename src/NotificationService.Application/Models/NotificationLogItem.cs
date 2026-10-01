namespace NotificationService.Application.Models;

/// <summary>
/// One notification audit record as shown in the notification log.
/// </summary>
public sealed class NotificationLogItem
{
    /// <summary>Notification id.</summary>
    public required string Id { get; init; }

    /// <summary>Channel name, for example "Email".</summary>
    public required string Channel { get; init; }

    /// <summary>Recipient address or comma-separated phone numbers.</summary>
    public required string Recipient { get; init; }

    /// <summary>Current status, for example "Sent" or "Failed".</summary>
    public required string Status { get; init; }

    /// <summary>When the notification was accepted (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>When the status last changed (UTC).</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Message id returned by the provider, if any.</summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>Error code when delivery failed, if any.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Error message when delivery failed, if any.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Number of delivery retries.</summary>
    public int RetryCount { get; init; }

    /// <summary>Whether the notification had an attachment.</summary>
    public bool HasAttachment { get; init; }
}
