using Azure;
using Azure.Messaging.ServiceBus;

namespace NotificationService.Infrastructure.Health;

/// <summary>
/// Turns a dependency exception into a short reason that is safe to show on the anonymous health endpoint
/// (status code, error code or exception type; never the message, which can contain endpoints or keys).
/// </summary>
internal static class HealthFailure
{
    /// <summary>
    /// Describes why a dependency call failed.
    /// </summary>
    /// <param name="exception">The exception thrown by the Azure SDK.</param>
    /// <returns>A short reason such as "403 AuthorizationFailure" or "MessagingEntityNotFound".</returns>
    public static string Reason(Exception exception) =>
        exception switch
        {
            RequestFailedException { ErrorCode: { Length: > 0 } code } failed => $"{failed.Status} {code}",
            RequestFailedException failed => $"HTTP {failed.Status}",
            ServiceBusException serviceBus => serviceBus.Reason.ToString(),
            UnauthorizedAccessException => "Unauthorized",
            _ => exception.GetType().Name
        };
}
