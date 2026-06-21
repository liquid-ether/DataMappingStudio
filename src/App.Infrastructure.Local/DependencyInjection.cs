using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Local;

/// <summary>
/// Composition root for the local store (Microsoft.Data.Sqlite generic repository, column catalog,
/// local change log, audit). Concrete services are registered here as the layer is built out
/// (Phase 2+).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddLocalStore(this IServiceCollection services)
    {
        // Phase 2 registers the generic SQLite repository, catalog and change-log services here.
        return services;
    }
}
