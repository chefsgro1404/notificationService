namespace NotificationService.Application.Interfaces;

public interface ITelegramSender
{
    Task<string?> SendAsync(
        string recipient,
        string text,
        Stream? attachment,
        string? fileName,
        CancellationToken cancellationToken);
}