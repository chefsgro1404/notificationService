using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Telegram;

/// <summary>
/// Telegram sender placeholder; not implemented yet.
/// </summary>
public class TelegramSender : ITelegramSender
{
    /// <inheritdoc />
    public Task<string?> SendAsync(string recipient, string text, Stream? attachment, string? fileName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
