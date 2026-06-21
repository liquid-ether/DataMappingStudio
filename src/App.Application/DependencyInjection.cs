using App.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Application;

/// <summary>
/// Composition root for the Application layer (use cases, engines, transport-agnostic services).
/// Hosts (App.Desktop, App.Web, App.Importer) call this plus the infrastructure registrations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        return services;
    }
}
