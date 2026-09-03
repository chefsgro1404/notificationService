namespace NotificationService.Infrastructure.Configuration;

public sealed class SmtpOptions
{
    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 2525;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public bool UseSsl { get; init; } = false;
}