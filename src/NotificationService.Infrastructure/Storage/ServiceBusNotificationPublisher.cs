using System.Text.Json;
using Azure.Messaging.ServiceBus;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;

namespace NotificationService.Infrastructure.Messaging;

/// <summary>
/// Publishes notification messages to the Service Bus queue named after the channel.
/// </summary>
public sealed class ServiceBusNotificationPublisher
    : INotificationPublisher
{
    private readonly ServiceBusClient _client;

    public ServiceBusNotificationPublisher(
        ServiceBusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task PublishAsync(
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var sender = _client.CreateSender(
            message.Channel.ToString().ToLowerInvariant());

        try
        {
            var body = JsonSerializer.Serialize(message);

            var serviceBusMessage =
                new ServiceBusMessage(body)
                {
                    ContentType = "application/json",
                    MessageId = message.NotificationId.ToString()
                };

            await sender.SendMessageAsync(
                serviceBusMessage,
                cancellationToken);
        }
        finally
        {
            await sender.DisposeAsync();
        }
    }
}