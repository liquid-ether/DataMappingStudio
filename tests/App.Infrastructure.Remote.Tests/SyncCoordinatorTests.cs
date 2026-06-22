using App.Application.Sync;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Infrastructure.Local;
using App.Infrastructure.Remote.Formats;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Remote.Tests;

/// <summary>
/// End-to-end sync across the real local store (SQLite change log = audit) and remote store
/// (per-writer logs + fold): publish clean, block + resolve a conflict, and auto-refresh.
/// </summary>
public sealed class SyncCoordinatorTests : IDisposable
{
    private const string Table = "widget";
    private readonly string _dir;
    private readonly LocalDatabase _db;
    private readonly SqliteLocalStore _store;
    private readonly SqliteCatalog _catalog;
    private readonly SqliteAuditLog _audit;
    private readonly FileRemoteStore _remote;
    private readonly SyncCoordinator _sync;

    public SyncCoordinatorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new LocalDatabase($"Data Source={Path.Combine(_dir, "local.db")}");
        _catalog = new SqliteCatalog(_db);
        _audit = new SqliteAuditLog(_db);
        FixedClock clock = new(RemoteTestData.T0);
        _store = new SqliteLocalStore(_db, _catalog, _audit, clock);
        _catalog.Seed([new ColumnCatalogEntry { TableName = Table, ColumnName = "name", ValueType = CatalogValueType.Text }]);
        _store.EnsureSchema();

        CsvRemoteFormat format = new();
        _remote = new FileRemoteStore(Path.Combine(_dir, "remote"), format);
        SnapshotBuilder snapshots = new(Path.Combine(_dir, "remote"), format);
        PublishService publish = new(_remote, snapshots, _catalog, new FieldMergeEngine(), clock);
        _sync = new SyncCoordinator(_catalog, _store, _audit, _remote, publish, new AutoRefreshPlanner()) { WriterId = "alice" };
    }

    [Fact]
    public void Clean_local_edit_publishes_to_the_remote_log()
    {
        Guid id = Guid.NewGuid();
        _store.Upsert(Table, new Row(Table, id) { ["name"] = "Alice" }, "cs1", "alice");

        Assert.True(_sync.PendingCount() > 0);
        PublishResult result = _sync.Publish([]);

        Assert.True(result.Published);
        Assert.Single(_remote.ReadWriterChanges("alice"));
        Assert.Equal(0, _sync.PendingCount());
    }

    [Fact]
    public void Divergent_remote_edit_blocks_then_keep_theirs_resolves()
    {
        Guid id = Guid.NewGuid();
        _remote.AppendChanges("bob", [RemoteTestData.Entry(Table, id, "name", null, "Bob", "bob", 1, 0)]);
        _store.Upsert(Table, new Row(Table, id) { ["name"] = "Alice" }, "cs1", "alice");

        PublishResult blocked = _sync.Publish([]);
        Assert.False(blocked.Published);
        MergeConflict conflict = Assert.Single(blocked.Conflicts);

        PublishResult resolved = _sync.Publish([new ConflictResolution(conflict.Cell, ConflictChoice.KeepTheirs)]);
        Assert.True(resolved.Published);
        Assert.Equal("Bob", _remote.ReadWriterChanges("alice").Single().NewValue);
    }

    [Fact]
    public void Refresh_fast_forwards_an_untouched_remote_row_into_the_local_store()
    {
        Guid id = Guid.NewGuid();
        _remote.AppendChanges("bob", [RemoteTestData.Entry(Table, id, "name", null, "Carol", "bob", 1, 0)]);

        RefreshResult result = _sync.Refresh();

        Assert.Equal(1, result.Applied);
        Assert.Equal("Carol", _store.GetById(Table, id)!["name"]);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
