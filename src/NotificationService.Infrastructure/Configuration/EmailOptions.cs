namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// Email delivery settings, including the provider to use ("Email" section).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; init; } = "Smtp";

    public string From { get; init; } = string.Empty;

    public SmtpOptions Smtp { get; init; } = new();
}