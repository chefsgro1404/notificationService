using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Services;

namespace NotificationService.Application.DependencyInjection;

/// <summary>
/// Registers application-layer services with the dependency injection container.
/// </summary>
public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Adds the application-layer services.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<
            ITextToSpeechService,
            TextToSpeechService>();

        services.AddScoped<
            IAudioCategoryService,
            AudioCategoryService>();

        return services;
    }
}
