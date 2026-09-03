using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Telegram;

public class TelegramSender : ITelegramSender
{
    public Task<string?> SendAsync(string recipient, string text, Stream? attachment, string? fileName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
