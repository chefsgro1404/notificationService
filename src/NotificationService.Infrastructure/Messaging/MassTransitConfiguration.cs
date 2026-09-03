using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Messaging;

public static class MassTransitConfiguration
{

    public static IServiceCollection AddNotificationMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(
            configuration.GetSection(
                ServiceBusOptions.SectionName));

        var options =
            configuration
                .GetSection(ServiceBusOptions.SectionName)
                .Get<ServiceBusOptions>()
            ?? throw new InvalidOperationException(
                "ServiceBus configuration is required.");

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "ServiceBus:ConnectionString is required.");
        }

        services.AddSingleton(
            new ServiceBusClient(options.ConnectionString));

        services.AddScoped<
            INotificationPublisher,
            ServiceBusNotificationPublisher>();

        return services;
    }
}