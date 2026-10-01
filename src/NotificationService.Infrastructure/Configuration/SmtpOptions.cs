namespace NotificationService.Infrastructure.Configuration;

/// <summary>
/// SMTP server settings used by the SMTP email provider ("Email:Smtp" section).
/// </summary>
public sealed class SmtpOptions
{
    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 2525;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public bool UseSsl { get; init; } = false;
}