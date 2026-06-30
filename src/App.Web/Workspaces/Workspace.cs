using App.Application.Abstractions;
using App.Application.Expressions;
using App.Application.Importing;
using App.Application.Provisioning;
using App.Application.Sync;
using App.Infrastructure.Local;

namespace App.Web.Workspaces;

/// <summary>Shared, stateless services every per-user workspace is built from (resolved once, reused).</summary>
public sealed record WorkspaceDependencies(
    IClock Clock,
    RuleExpressionBuilder RuleBuilder,
    AutoRefreshPlanner Planner,
    FieldMergeEngine Merge,
    IRemoteStore Remote,
    ISnapshotBuilder Snapshots);

/// <summary>
/// One user's isolated working copy on the web host — its own SQLite <c>local.db</c>, catalog, audit log,
/// store, import engine and sync coordinator, exactly like a desktop analyst. The shared remote folder is
/// the source of truth; this local copy is a disposable cache, built by refolding the remote on creation.
/// The coordinator's writer id is host-namespaced (<c>user@host</c>) so multiple hosts never collide on a
/// user's per-writer remote log.
/// </summary>
public sealed class Workspace : IDisposable
{
    private readonly LocalDatabase _db;

    public string WriterId { get; }
    public ICatalog Catalog { get; }
    public IAuditLog Audit { get; }
    public ILocalStore Store { get; }
    public ISyncCoordinator Coordinator { get; }
    public ImportEngine ImportEngine { get; }
    public DateTimeOffset LastAccessUtc { get; set; }

    public Workspace(string databasePath, string writerId, WorkspaceDependencies deps)
    {
        WriterId = writerId;
        _db = new LocalDatabase($"Data Source={databasePath}");

        SqliteCatalog catalog = new(_db);
        SqliteAuditLog audit = new(_db);
        Catalog = catalog;
        Audit = audit;
        Store = new SqliteLocalStore(_db, catalog, audit, deps.Clock);

        // Provision this user's working copy from the catalog, then pull the shared canonical state in.
        catalog.Seed(DefaultCatalog.Entries());
        Store.EnsureSchema();

        IPublishService publish = new PublishService(deps.Remote, deps.Snapshots, Catalog, deps.Merge, deps.Clock);
        Coordinator = new SyncCoordinator(Catalog, Store, Audit, deps.Remote, publish, deps.Planner) { WriterId = writerId };
        ImportEngine = new ImportEngine(Catalog, Store, deps.RuleBuilder);

        Coordinator.Refresh(); // build the local cache from the remote fold (no-op-safe if the remote is down)
        LastAccessUtc = DateTimeOffset.UtcNow;
    }

    public void Dispose() => _db.Dispose();
}
