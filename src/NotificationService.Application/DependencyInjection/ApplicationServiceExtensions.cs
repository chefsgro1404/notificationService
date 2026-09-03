using Microsoft.Extensions.DependencyInjection;

namespace NotificationService.Application.DependencyInjection;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        return services;
    }
}