using App.Application.Sync;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Infrastructure.Remote.Formats;
using static App.Infrastructure.Remote.Tests.RemoteTestData;

namespace App.Infrastructure.Remote.Tests;

public class PublishServiceTests
{
    private const string Table = "widget";
    private static readonly Guid R1 = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly CellKey NameCell = new(Table, R1, "name");

    private static (PublishService Service, FileRemoteStore Store, SnapshotBuilder Snapshots) NewService(string dir)
    {
        CsvRemoteFormat format = new();
        FileRemoteStore store = new(dir, format);
        SnapshotBuilder snapshots = new(dir, format);
        FakeCatalog catalog = new([new ColumnCatalogEntry { TableName = Table, ColumnName = "name", ValueType = CatalogValueType.Text }]);
        PublishService service = new(store, snapshots, catalog, new FieldMergeEngine(), new FixedClock(T0));
        return (service, store, snapshots);
    }

    [Fact]
    public void Clean_publish_appends_to_own_log_and_rebuilds_snapshot()
    {
        using TempFolder dir = new();
        (PublishService service, FileRemoteStore store, SnapshotBuilder snapshots) = NewService(dir.Path);

        List<ChangeLogEntry> pending = [Entry(Table, R1, "name", null, "Alice", "alice", 1, 0, ChangeOperation.Insert)];
        PublishResult result = service.Publish("alice", pending, []);

        Assert.True(result.Published);
        Assert.Equal(1, result.AppendedCount);
        Assert.Single(store.ReadWriterChanges("alice"));
        Assert.Contains(snapshots.ReadSnapshot(Table)!.Rows, r => r.Contains("Alice"));
    }

    [Fact]
    public void Divergent_remote_change_blocks_publish_until_resolved()
    {
        using TempFolder dir = new();
        (PublishService service, FileRemoteStore store, _) = NewService(dir.Path);

        // Bob already published name = "Bob" to the canonical store.
        store.AppendChanges("bob", [Entry(Table, R1, "name", null, "Bob", "bob", 1, 0, ChangeOperation.Insert)]);

        // Alice tries to publish her own name = "Alice" (started from empty base).
        List<ChangeLogEntry> pending = [Entry(Table, R1, "name", null, "Alice", "alice", 1, 1, ChangeOperation.Insert)];

        PublishResult blocked = service.Publish("alice", pending, []);
        Assert.False(blocked.Published);
        MergeConflict conflict = Assert.Single(blocked.Conflicts);
        Assert.Equal("Bob", conflict.Remote);
        Assert.Empty(store.ReadWriterChanges("alice"));
    }

    [Fact]
    public void Keep_theirs_resolution_publishes_the_remote_value()
    {
        using TempFolder dir = new();
        (PublishService service, FileRemoteStore store, SnapshotBuilder snapshots) = NewService(dir.Path);
        store.AppendChanges("bob", [Entry(Table, R1, "name", null, "Bob", "bob", 1, 0, ChangeOperation.Insert)]);
        List<ChangeLogEntry> pending = [Entry(Table, R1, "name", null, "Alice", "alice", 1, 1, ChangeOperation.Insert)];

        PublishResult resolved = service.Publish("alice", pending, [new ConflictResolution(NameCell, ConflictChoice.KeepTheirs)]);

        Assert.True(resolved.Published);
        ChangeLogEntry appended = Assert.Single(store.ReadWriterChanges("alice"));
        Assert.Equal("Bob", appended.NewValue);
        Assert.Contains(snapshots.ReadSnapshot(Table)!.Rows, r => r.Contains("Bob"));
    }
}
