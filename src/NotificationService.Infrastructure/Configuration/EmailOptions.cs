namespace NotificationService.Infrastructure.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; init; } = "Smtp";

    public string From { get; init; } = string.Empty;

    public SmtpOptions Smtp { get; init; } = new();
}