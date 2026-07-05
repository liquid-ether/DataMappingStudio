using App.Application.Abstractions;
using App.Application.Catalog;
using App.Application.Sync;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Infrastructure.Local;
using App.Infrastructure.Remote.Formats;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Remote.Tests;

/// <summary>
/// The meta-model sync loop end to end: an admin on one working copy creates a table (with a reference +
/// a key-lookup column) and publishes rows; a completely fresh working copy over the same shared folder
/// refreshes and receives the table, its metadata, its columns AND the data — the regression that data
/// for unknown tables/columns used to be silently skipped forever.
/// </summary>
public sealed class CatalogSyncTests : IDisposable
{
    private readonly string _dir;
    private readonly string _remote;
    private readonly List<LocalDatabase> _dbs = [];

    public CatalogSyncTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-catsync-" + Guid.NewGuid().ToString("N"));
        _remote = Path.Combine(_dir, "remote");
        Directory.CreateDirectory(_remote);
    }

    private sealed record Stack(
        SqliteCatalog Catalog,
        SqliteLocalStore Store,
        SyncCoordinator Sync,
        MetaModelService MetaModel,
        CatalogSyncService CatalogSync);

    private Stack BuildStack(string writer, CatalogSyncService catalogSync)
    {
        LocalDatabase db = new($"Data Source={Path.Combine(_dir, writer + ".db")}");
        _dbs.Add(db);
        SqliteCatalog catalog = new(db);
        SqliteAuditLog audit = new(db);
        FixedClock clock = new(RemoteTestData.T0);
        SqliteLocalStore store = new(db, catalog, audit, clock);

        // Same provisioning order as a real workspace: defaults → shared runtime meta-model → schema.
        catalog.Seed(App.Application.Provisioning.DefaultCatalog.Entries());
        catalog.SeedTableMeta(App.Application.Provisioning.DefaultCatalog.TableMeta());
        catalogSync.ApplyTo(catalog, catalog, store);
        store.EnsureSchema();

        CsvRemoteFormat format = new();
        FileRemoteStore remote = new(_remote, format);
        PublishService publish = new(remote, new SnapshotBuilder(_remote, format), catalog, new FieldMergeEngine(), clock);
        SyncCoordinator sync = new(catalog, store, audit, remote, publish, new AutoRefreshPlanner(), null, catalogSync, catalog) { WriterId = writer };
        MetaModelService metaModel = new(catalog, catalog, store, new EnvironmentCurrentUser(), catalogSync, sync);
        return new Stack(catalog, store, sync, metaModel, catalogSync);
    }

    [Fact]
    public void A_created_table_with_lookup_columns_and_its_data_reach_a_fresh_working_copy()
    {
        CatalogSyncService catalogSyncA = new(new FileCatalogRemote(_remote));
        Stack alice = BuildStack("alice", catalogSyncA);

        // Alice creates a runtime table: a code, a reference to application, and a key-lookup column.
        alice.MetaModel.CreateTable(
            new TableCatalogEntry
            {
                TableName = "vendor",
                LabelEn = "Vendors",
                LabelFr = "Fournisseurs",
                NavVisible = true,
                NavOrder = 50,
                DisplayColumn = "code",
                IsUserAdded = true,
            },
            [
                new ColumnCatalogEntry { TableName = "vendor", ColumnName = "code", ValueType = CatalogValueType.Text, IsRequired = true, IsUserAdded = true },
                new ColumnCatalogEntry { TableName = "vendor", ColumnName = "app_id", Kind = ColumnKind.Reference, ValueType = CatalogValueType.Uuid, ReferenceTarget = "application", IsUserAdded = true },
                new ColumnCatalogEntry { TableName = "vendor", ColumnName = "app_desc", Kind = ColumnKind.Computed, Formula = "lookup(code, application.app_code, description)", IsUserAdded = true },
            ]);

        // She can use it immediately…
        Guid rowId = Guid.NewGuid();
        alice.Store.Upsert("vendor", new Row("vendor", rowId) { ["code"] = "V-001" }, "cs1", "alice");
        Assert.True(alice.Sync.Publish([]).Published);

        // …and a brand-new working copy (fresh db + its own host-level fold cache) converges fully.
        CatalogSyncService catalogSyncB = new(new FileCatalogRemote(_remote)); // separate host
        Stack bob = BuildStack("bob", catalogSyncB);

        Assert.Contains("vendor", bob.Catalog.GetTables());
        TableCatalogEntry meta = bob.Catalog.GetTableMeta("vendor")!;
        Assert.Equal("Vendors", meta.LabelEn);
        Assert.True(meta.NavVisible);
        Assert.Equal("code", meta.DisplayColumn);

        IReadOnlyList<ColumnCatalogEntry> columns = bob.Catalog.GetForTable("vendor");
        Assert.Contains(columns, c => c is { ColumnName: "app_id", Kind: ColumnKind.Reference, ReferenceTarget: "application" });
        Assert.Contains(columns, c => c is { ColumnName: "app_desc", Kind: ColumnKind.Computed });

        bob.Sync.Refresh();
        Assert.Contains(bob.Store.GetAll("vendor"), r => r["code"] == "V-001");
    }

    [Fact]
    public void A_column_added_after_the_fact_reaches_an_already_running_copy_before_its_data()
    {
        CatalogSyncService syncA = new(new FileCatalogRemote(_remote));
        CatalogSyncService syncB = new(new FileCatalogRemote(_remote));
        Stack alice = BuildStack("alice", syncA);
        Stack bob = BuildStack("bob", syncB); // both up BEFORE the change

        alice.MetaModel.AddColumn(new ColumnCatalogEntry
        {
            TableName = "application",
            ColumnName = "tier",
            ValueType = CatalogValueType.Text,
            IsUserAdded = true,
        });
        Guid id = Guid.NewGuid();
        alice.Store.Upsert("application", new Row("application", id) { ["app_code"] = "APP1", ["tier"] = "gold" }, "cs1", "alice");
        Assert.True(alice.Sync.Publish([]).Published);

        // Bob's next refresh applies the catalog change first, then adopts the data including the new column.
        bob.Sync.Refresh();
        Assert.Contains(bob.Catalog.GetForTable("application"), c => c.ColumnName == "tier");
        Row adopted = Assert.Single(bob.Store.GetAll("application"), r => r["app_code"] == "APP1");
        Assert.Equal("gold", adopted["tier"]);
    }

    [Fact]
    public void Meta_model_writes_without_permission_are_rejected()
    {
        CatalogSyncService sync = new(new FileCatalogRemote(_remote));
        Stack alice = BuildStack("alice", sync);
        MetaModelService restricted = new(alice.Catalog, alice.Catalog, alice.Store, new NoPermissionUser(), sync, alice.Sync);

        Assert.Throws<UnauthorizedAccessException>(() => restricted.AddColumn(new ColumnCatalogEntry
        {
            TableName = "application",
            ColumnName = "blocked",
            ValueType = CatalogValueType.Text,
        }));
    }

    [Theory]
    [InlineData("column_catalog")]
    [InlineData("sqlite_master")]
    [InlineData("settings")]
    [InlineData("has space")]
    public void Reserved_or_invalid_table_names_are_rejected(string name)
    {
        CatalogSyncService sync = new(new FileCatalogRemote(_remote));
        Stack alice = BuildStack("alice", sync);

        Assert.Throws<ArgumentException>(() => alice.MetaModel.CreateTable(
            new TableCatalogEntry { TableName = name, LabelEn = name, IsUserAdded = true },
            [new ColumnCatalogEntry { TableName = name, ColumnName = "x", ValueType = CatalogValueType.Text }]));
    }

    private sealed class NoPermissionUser : ICurrentUser
    {
        public string UserId => "nobody";
        public string Name => "nobody";
        public string DisplayName => "nobody";
        public IReadOnlyCollection<string> Roles => [];
        public bool HasPermission(string permission) => false;
    }

    public void Dispose()
    {
        foreach (LocalDatabase db in _dbs)
        {
            db.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
