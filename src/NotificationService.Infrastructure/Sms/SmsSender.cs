using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Sms;

public class SmsSender : ISmsSender
{
    /// <inheritdoc />
    public Task<string?> SendAsync(string recipient, string text, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
