using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.Email;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Storage;

namespace NotificationService.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ----------------------------------------
        // Configuration
        // ----------------------------------------

        services.Configure<BlobStorageOptions>(
            configuration.GetSection(
                BlobStorageOptions.SectionName));

        services.Configure<AuditStorageOptions>(
            configuration.GetSection(
                AuditStorageOptions.SectionName));

        services.Configure<EmailOptions>(
            configuration.GetSection(
                EmailOptions.SectionName));

        services.Configure<
            AzureCommunicationServicesOptions>(
            configuration.GetSection(
                AzureCommunicationServicesOptions.SectionName));

        services.Configure<ServiceBusOptions>(
            configuration.GetSection(
                ServiceBusOptions.SectionName));


        // ----------------------------------------
        // Blob Storage
        // ----------------------------------------

        services.AddSingleton<
            IBlobStorage,
            AzureBlobStorage>();


        // ----------------------------------------
        // Audit Storage
        // ----------------------------------------

        services.AddSingleton<
            IAuditStore,
            AzureTableAuditStore>();


        // ----------------------------------------
        // Email Provider
        // ----------------------------------------

        RegisterEmailProvider(
            services,
            configuration);


        // ----------------------------------------
        // MassTransit
        // ----------------------------------------

        services.AddNotificationMessaging(
            configuration);


        return services;
    }


    private static void RegisterEmailProvider(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var provider =
            configuration["Email:Provider"];

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                "Email:Provider is required.");
        }


        if (string.Equals(
                provider,
                "Smtp",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<
                IEmailSender,
                Smtp4DevEmailSender>();

            return;
        }


        if (string.Equals(
                provider,
                "AzureCommunicationServices",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<
                IEmailSender,
                AzureCommunicationEmailSender>();

            return;
        }


        throw new InvalidOperationException(
            $"Unsupported email provider '{provider}'. " +
            "Supported providers are: " +
            "'Smtp', 'AzureCommunicationServices'.");
    }
}   