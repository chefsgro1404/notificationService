using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Sms;

/// <summary>
/// SMS sender placeholder; not implemented yet.
/// </summary>
public class SmsSender : ISmsSender
{
    /// <inheritdoc />
    public Task<string?> SendAsync(string recipient, string text, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
