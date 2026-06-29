using App.Application.Abstractions;
using App.Application.Sync;
using App.Infrastructure.Remote.Formats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Infrastructure.Remote;

/// <summary>
/// Composition root for the remote store: per-writer append-only logs, deterministic fold, snapshot
/// builder, the pluggable <see cref="IRemoteFormat"/> providers, and the publish service. Pass the
/// synced canonical folder and the configured format name to wire concrete services; with no folder the
/// seam is a no-op (hosts without a remote configured).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddRemoteStore(
        this IServiceCollection services,
        string? canonicalFolder = null,
        string formatName = "parquet",
        bool enableAutoRefresh = true)
    {
        // Format providers are always available so format conversion utilities can resolve any of them.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteFormat, ParquetRemoteFormat>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteFormat, CsvRemoteFormat>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteFormat, ExcelRemoteFormat>());
        services.TryAddSingleton<IRemoteFormatProvider, RemoteFormatProvider>();

        if (canonicalFolder is null)
        {
            return services;
        }

        services.TryAddSingleton<IRemoteStore>(sp =>
            new FileRemoteStore(canonicalFolder, sp.GetRequiredService<IRemoteFormatProvider>().Resolve(formatName)));
        services.TryAddSingleton<ISnapshotBuilder>(sp =>
            new SnapshotBuilder(canonicalFolder, sp.GetRequiredService<IRemoteFormatProvider>().Resolve(formatName)));
        services.TryAddSingleton<IPublishService, PublishService>();
        // The writer id keys this working copy's per-writer remote log — a host-level identity (the OS
        // account), distinct from the per-edit ICurrentUser. SyncCoordinator is a singleton, so it cannot
        // depend on the scoped ICurrentUser; the OS account is correct for the single-writer desktop.
        services.TryAddSingleton<ISyncCoordinator>(sp => new SyncCoordinator(
            sp.GetRequiredService<ICatalog>(),
            sp.GetRequiredService<ILocalStore>(),
            sp.GetRequiredService<IAuditLog>(),
            sp.GetRequiredService<IRemoteStore>(),
            sp.GetRequiredService<IPublishService>(),
            sp.GetRequiredService<AutoRefreshPlanner>())
        {
            WriterId = EnvironmentCurrentUser.Sanitize(Environment.UserName),
        });

        // Background auto-refresh (no-op-safe; flags conflicts rather than clobbering local edits).
        if (enableAutoRefresh)
        {
            services.AddHostedService<AutoRefreshService>();
        }

        return services;
    }
}
