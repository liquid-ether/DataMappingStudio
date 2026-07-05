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
        bool enableAutoRefresh = true,
        bool enableCoordinator = true)
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

        // Meta-model sync: per-writer catalog change logs under _meta/catalog (host-wide fold cache).
        services.TryAddSingleton<ICatalogRemote>(_ => new FileCatalogRemote(canonicalFolder));
        services.TryAddSingleton<CatalogSyncService>();

        // Shared runtime settings document (_meta/settings.json); each host wires IRuntimeConfig over it.
        services.TryAddSingleton<ISharedSettingsStore>(_ => new FileSettingsStore(canonicalFolder));

        // Named import mappings (_meta/import-mappings) — authored in the wizard, consumed by wizard + CLI.
        services.TryAddSingleton<App.Application.Importing.IImportMappingStore>(_ => new FileImportMappingStore(canonicalFolder));

        // Per-user hosts (web) build a PublishService + SyncCoordinator per workspace instead of these
        // singletons, because both bind to a (per-user) catalog/store; the shared remote store + snapshot
        // builder above are still singletons over the one shared folder.
        if (!enableCoordinator)
        {
            return services;
        }

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
            sp.GetRequiredService<AutoRefreshPlanner>(),
            foldCache: null,
            sp.GetRequiredService<CatalogSyncService>(),
            sp.GetService<ITableCatalog>()) // meta-model changes arrive via refresh (desktop has no admin UI)
        {
            WriterId = EnvironmentCurrentUser.Sanitize(Environment.UserName),
        });

        // The single write path for meta-model changes (grid add-column on the desktop routes through it
        // so a locally added column reaches the team like any other change). OS-account identity, same
        // as the coordinator's writer id — the desktop is single-user and fully privileged.
        services.TryAddSingleton(sp => new App.Application.Catalog.MetaModelService(
            sp.GetRequiredService<ICatalog>(),
            sp.GetRequiredService<ITableCatalog>(),
            sp.GetRequiredService<ILocalStore>(),
            new EnvironmentCurrentUser(),
            sp.GetRequiredService<CatalogSyncService>(),
            sp.GetRequiredService<ISyncCoordinator>()));

        // Background auto-refresh (no-op-safe; flags conflicts rather than clobbering local edits).
        if (enableAutoRefresh)
        {
            services.AddHostedService<AutoRefreshService>();
        }

        return services;
    }
}
