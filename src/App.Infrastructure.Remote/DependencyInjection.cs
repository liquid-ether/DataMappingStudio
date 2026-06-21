using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Remote;

/// <summary>
/// Composition root for the remote store (per-writer append-only logs, deterministic fold,
/// materialized snapshots and the pluggable IRemoteFormat providers). Concrete services are
/// registered here as the layer is built out (Phase 4+).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddRemoteStore(this IServiceCollection services)
    {
        // Phase 4 registers the log store, fold engine, snapshot builder and format providers here.
        return services;
    }
}
