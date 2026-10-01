namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Voice call settings: provider, audio URL lifetime and retry attempts ("Voice" section).
/// The audio played on a call comes from the notification's audio category, not from settings.
/// </summary>
public sealed class VoiceOptions
{
    public const string SectionName = "Voice";

    /// <summary>"AzureCommunicationServices" places real calls; "Mock" only logs.</summary>
    public string Provider { get; init; } = "Mock";

    /// <summary>How long the read-only audio URL given to the call provider stays valid.</summary>
    public int AudioUrlValidityMinutes { get; init; } = 60;

    /// <summary>
    /// Total call attempts per recipient, including the first call. An unanswered call is retried until this
    /// is reached; 0 (the default) or 1 means each recipient is called once and never retried.
    /// </summary>
    public int MaxCallAttempts { get; init; }
}
