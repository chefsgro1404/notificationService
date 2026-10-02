using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.Email;
using NotificationService.Infrastructure.Health;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Speech;
using NotificationService.Infrastructure.Storage;
using NotificationService.Infrastructure.Voice;

namespace NotificationService.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services (storage, audit, email, voice, speech, messaging and health checks) with dependency injection.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Adds infrastructure services and binds their configuration sections.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
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

        services.Configure<VoiceOptions>(
            configuration.GetSection(
                VoiceOptions.SectionName));

        services.Configure<SpeechOptions>(
            configuration.GetSection(
                SpeechOptions.SectionName));


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
        // Text-to-Speech Log and Audio Categories (same storage account, own tables)
        // ----------------------------------------

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<
            ITextToSpeechLogStore,
            AzureTableTextToSpeechLogStore>();

        services.AddSingleton<
            IAudioCategoryStore,
            AzureTableAudioCategoryStore>();


        // ----------------------------------------
        // Email Provider
        // ----------------------------------------

        RegisterEmailProvider(
            services,
            configuration);


        // ----------------------------------------
        // Voice Provider
        // ----------------------------------------

        RegisterVoiceProvider(
            services,
            configuration);


        // ----------------------------------------
        // Text-to-Speech (Azure AI Speech, always the real service)
        // ----------------------------------------

        var speechTimeoutSeconds =
            configuration.GetValue(
                $"{SpeechOptions.SectionName}:{nameof(SpeechOptions.TimeoutSeconds)}",
                30);

        services.AddHttpClient<
                ISpeechSynthesizer,
                AzureSpeechSynthesizer>(client =>
            client.Timeout = TimeSpan.FromSeconds(speechTimeoutSeconds));


        // ----------------------------------------
        // MassTransit
        // ----------------------------------------

        services.AddNotificationMessaging(
            configuration);


        // ----------------------------------------
        // Health checks (read-only, cached; see /api/health)
        // ----------------------------------------

        AddDependencyHealthChecks(
            services,
            configuration);


        return services;
    }


    private static void AddDependencyHealthChecks(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HealthCheckOptions>(
            configuration.GetSection(
                HealthCheckOptions.SectionName));

        var timeout = TimeSpan.FromSeconds(
            configuration.GetValue(
                $"{HealthCheckOptions.SectionName}:{nameof(HealthCheckOptions.TimeoutSeconds)}",
                10));

        // Singletons so the Azure clients inside each check are created once, not on every run.
        services.AddSingleton<ServiceBusHealthCheck>();
        services.AddSingleton<StorageHealthCheck>();
        services.AddSingleton<AcsHealthCheck>();
        services.AddSingleton<HealthReportCache>();

        services.AddHealthChecks()
            .AddCheck<ServiceBusHealthCheck>("serviceBus", timeout: timeout)
            .AddCheck<StorageHealthCheck>("storage", timeout: timeout)
            .AddCheck<AcsHealthCheck>("acs", timeout: timeout);
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


    private static void RegisterVoiceProvider(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var provider =
            configuration["Voice:Provider"];

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                "Voice:Provider is required.");
        }


        if (string.Equals(
                provider,
                "Mock",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<
                IVoiceCallSender,
                MockVoiceCallSender>();

            return;
        }


        if (string.Equals(
                provider,
                "AzureCommunicationServices",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<
                IVoiceCallSender,
                AzureCommunicationVoiceCallSender>();

            return;
        }


        throw new InvalidOperationException(
            $"Unsupported voice provider '{provider}'. " +
            "Supported providers are: " +
            "'Mock', 'AzureCommunicationServices'.");
    }
}   