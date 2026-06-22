using App.Application.Mappings;
using App.Application.References;
using App.Application.Sync;
using App.Domain.Entities;
using App.Infrastructure.Local;
using App.Infrastructure.Remote.Formats;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Remote.Tests;

/// <summary>
/// Proves the whole point of the persistence layer: a Mapping Studio edit written via the repository
/// becomes a pending local change that the publish flow pushes to the remote store.
/// </summary>
public sealed class MappingPublishTests : IDisposable
{
    private readonly string _dir;
    private readonly LocalDatabase _db;

    public MappingPublishTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-mappub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new LocalDatabase($"Data Source={Path.Combine(_dir, "local.db")}");
    }

    [Fact]
    public void A_mapping_edit_is_pending_then_publishes_to_the_remote_log()
    {
        SqliteCatalog catalog = new(_db);
        catalog.Seed(App.Application.Provisioning.DefaultCatalog.Entries());
        SqliteAuditLog audit = new(_db);
        SqliteLocalStore store = new(_db, catalog, audit, new FixedClock(RemoteTestData.T0));
        store.EnsureSchema();

        MappingRepository repo = new(store);
        repo.Save(new MappingRecord(Guid.NewGuid(), "CUSTOMER_360", "email", MappingKind.Field, "text", "a.email"), "alice");

        CsvRemoteFormat format = new();
        FileRemoteStore remote = new(Path.Combine(_dir, "remote"), format);
        SnapshotBuilder snapshots = new(Path.Combine(_dir, "remote"), format);
        PublishService publish = new(remote, snapshots, catalog, new FieldMergeEngine(), new FixedClock(RemoteTestData.T0));
        SyncCoordinator sync = new(catalog, store, audit, remote, publish, new AutoRefreshPlanner()) { WriterId = "alice" };

        Assert.True(sync.PendingCount() > 0);
        PublishResult result = sync.Publish([]);

        Assert.True(result.Published);
        Assert.Contains(remote.ReadWriterChanges("alice"), e => e.Table == TableNames.Mapping && e.Column == "expression" && e.NewValue == "a.email");
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
