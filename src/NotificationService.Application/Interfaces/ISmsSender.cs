namespace NotificationService.Application.Interfaces;

public interface ISmsSender
{
    Task<string?> SendAsync(
        string recipient,
        string text,
        CancellationToken cancellationToken);
}