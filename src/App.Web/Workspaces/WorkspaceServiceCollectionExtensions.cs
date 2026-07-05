using App.Application.Abstractions;
using App.Application.Expressions;
using App.Application.Importing;
using App.Application.Sync;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Web.Workspaces;

/// <summary>
/// Wires the web host for <b>per-user workspaces</b>: the catalog / store / audit / sync coordinator /
/// import engine become <em>scoped</em> services resolved from the current user's <see cref="Workspace"/>,
/// so the shared App.UI components (which inject <c>ILocalStore</c> etc.) transparently operate on each
/// user's own working copy. Requires the shared remote store (AddRemoteStore with
/// <c>enableCoordinator: false</c>) and the stateless engines (AddApplication) to be registered already.
/// </summary>
public static class WorkspaceServiceCollectionExtensions
{
    public static IServiceCollection AddPerUserWorkspaces(this IServiceCollection services, string usersRoot, string? host = null)
    {
        Directory.CreateDirectory(usersRoot);
        string hostName = WorkspaceRegistry.Sanitize(host ?? Environment.MachineName);

        services.AddSingleton<IWorkspaceRegistry>(sp => new WorkspaceRegistry(usersRoot, hostName, new WorkspaceDependencies(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<RuleExpressionBuilder>(),
            sp.GetRequiredService<AutoRefreshPlanner>(),
            sp.GetRequiredService<FieldMergeEngine>(),
            sp.GetRequiredService<IRemoteStore>(),
            sp.GetRequiredService<ISnapshotBuilder>(),
            new RemoteFoldCache(), // one data fold per remote change for the whole host, not one per workspace
            sp.GetRequiredService<CatalogSyncService>()))); // shared meta-model fold, same idea

        services.AddScoped<IWorkspaceAccessor, WorkspaceAccessor>();

        // Track live circuits per user so the idle sweeper never evicts a workspace an open session still
        // holds (its scoped services are cached for the circuit's lifetime).
        services.AddSingleton<WorkspaceLiveness>();
        services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, WorkspaceCircuitHandler>();

        // The per-user working copy: every DB-bound service resolves from the current user's workspace.
        services.AddScoped<ICatalog>(sp => sp.GetRequiredService<IWorkspaceAccessor>().Current.Catalog);
        services.AddScoped<ITableCatalog>(sp => sp.GetRequiredService<IWorkspaceAccessor>().Current.TableCatalog);
        services.AddScoped<ILocalStore>(sp => sp.GetRequiredService<IWorkspaceAccessor>().Current.Store);
        services.AddScoped<IAuditLog>(sp => sp.GetRequiredService<IWorkspaceAccessor>().Current.Audit);
        services.AddScoped<ISyncCoordinator>(sp => sp.GetRequiredService<IWorkspaceAccessor>().Current.Coordinator);

        // Meta-model writes: per-user catalog/store + the shared catalog sync (admin Model module +
        // grid add-column both route through this).
        services.AddScoped(sp => new App.Application.Catalog.MetaModelService(
            sp.GetRequiredService<ICatalog>(),
            sp.GetRequiredService<ITableCatalog>(),
            sp.GetRequiredService<ILocalStore>(),
            sp.GetRequiredService<App.Application.Abstractions.ICurrentUser>(),
            sp.GetRequiredService<CatalogSyncService>(),
            sp.GetRequiredService<ISyncCoordinator>()));

        // ImportEngine is registered as a singleton by AddApplication; per-user it must follow the store.
        services.RemoveAll<ImportEngine>();
        services.AddScoped(sp => sp.GetRequiredService<IWorkspaceAccessor>().Current.ImportEngine);

        services.AddHostedService<PerUserAutoRefreshService>();
        return services;
    }
}
