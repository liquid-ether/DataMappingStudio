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

    [Fact]
    public void Refresh_skips_a_refold_when_the_remote_is_unchanged_and_tracks_health()
    {
        Guid id = Guid.NewGuid();
        _remote.AppendChanges("bob", [RemoteTestData.Entry(Table, id, "name", null, "Carol", "bob", 1, 0)]);
        string version1 = _remote.RemoteVersion();

        Assert.Equal(1, _sync.Refresh().Applied);          // first refresh pulls the remote change
        Assert.True(_sync.RemoteStatus.Available);
        Assert.NotNull(_sync.RemoteStatus.LastSuccessUtc);

        Assert.Equal(0, _sync.Refresh().Applied);          // unchanged remote -> nothing to apply (skipped)

        Guid id2 = Guid.NewGuid();
        _remote.AppendChanges("bob", [RemoteTestData.Entry(Table, id2, "name", null, "Dave", "bob", 2, 0)]);
        Assert.NotEqual(version1, _remote.RemoteVersion()); // the cheap signature changed after the append
        Assert.Equal(1, _sync.Refresh().Applied);           // and the next refresh refolds and applies it
    }

    [Fact]
    public void Refresh_marks_the_remote_unavailable_when_the_folder_is_inaccessible()
    {
        ThrowingRemote throwing = new();
        CsvRemoteFormat format = new();
        PublishService publish = new(throwing, new SnapshotBuilder(Path.Combine(_dir, "r2"), format), _catalog, new FieldMergeEngine(), new FixedClock(RemoteTestData.T0));
        SyncCoordinator sync = new(_catalog, _store, _audit, throwing, publish, new AutoRefreshPlanner()) { WriterId = "alice" };

        RefreshResult result = sync.Refresh();

        Assert.Equal(0, result.Applied);                    // no throw; degrades gracefully
        Assert.False(sync.RemoteStatus.Available);
        Assert.Equal("offline", sync.RemoteStatus.Message);
    }

    [Fact]
    public void Publish_checkpoint_survives_a_restart_so_published_edits_never_look_pending_again()
    {
        Guid id = Guid.NewGuid();
        _store.Upsert(Table, new Row(Table, id) { ["name"] = "Alice" }, "cs1", "alice");
        Assert.True(_sync.Publish([]).Published);
        Assert.Equal(0, _sync.PendingCount());

        // Simulate a restart / workspace re-creation: a brand-new coordinator over the same working copy.
        CsvRemoteFormat format = new();
        PublishService publish = new(_remote, new SnapshotBuilder(Path.Combine(_dir, "remote"), format), _catalog, new FieldMergeEngine(), new FixedClock(RemoteTestData.T0));
        SyncCoordinator restarted = new(_catalog, _store, _audit, _remote, publish, new AutoRefreshPlanner()) { WriterId = "alice" };

        // Without the persisted checkpoint this was the whole history (inflated badge + spurious conflicts).
        Assert.Equal(0, restarted.PendingCount());

        // New edits after the restart are pending as normal; publishing advances the checkpoint again.
        _store.Upsert(Table, new Row(Table, id) { ["name"] = "Alice 2" }, "cs2", "alice");
        Assert.Equal(1, restarted.PendingCount());
        Assert.True(restarted.Publish([]).Published);
        Assert.Equal(0, restarted.PendingCount());
    }

    private sealed class ThrowingRemote : App.Application.Abstractions.IRemoteStore
    {
        public IReadOnlyList<ChangeLogEntry> ReadAllChanges() => throw new IOException("offline");
        public IReadOnlyList<ChangeLogEntry> ReadWriterChanges(string writerId) => throw new IOException("offline");
        public void AppendChanges(string writerId, IReadOnlyList<ChangeLogEntry> entries) => throw new IOException("offline");
        public IReadOnlyList<string> Writers() => throw new IOException("offline");
        public string RemoteVersion() => throw new IOException("offline");
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
