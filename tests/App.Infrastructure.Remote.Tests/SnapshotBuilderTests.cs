using App.Domain.Catalog;
using App.Domain.Data;
using App.Infrastructure.Remote.Formats;
using static App.Infrastructure.Remote.Tests.RemoteTestData;

namespace App.Infrastructure.Remote.Tests;

public class SnapshotBuilderTests
{
    private const string Table = "widget";
    private static readonly Guid R1 = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid R2 = Guid.Parse("11111111-0000-0000-0000-000000000002");

    private static readonly IReadOnlyList<RemoteColumn> Columns =
    [
        new("name", CatalogValueType.Text),
        new("qty", CatalogValueType.Integer),
    ];

    private static List<ChangeLogEntry> Batch1() =>
    [
        Entry(Table, R1, "name", null, "Alice", "alice", 1, 0, ChangeOperation.Insert),
        Entry(Table, R1, "qty", null, "5", "alice", 2, 0, ChangeOperation.Insert),
    ];

    private static List<ChangeLogEntry> Batch2() =>
    [
        Entry(Table, R1, "name", "Alice", "Bob", "alice", 3, 1),
        Entry(Table, R2, "name", null, "Carol", "alice", 4, 2, ChangeOperation.Insert),
    ];

    [Fact]
    public void Full_build_materializes_current_rows()
    {
        using TempFolder dir = new();
        SnapshotBuilder builder = new(dir.Path, new CsvRemoteFormat());

        builder.Rebuild(Table, Columns, Batch1());

        RemoteTable? snapshot = builder.ReadSnapshot(Table);
        Assert.NotNull(snapshot);
        IReadOnlyList<string?> row = Assert.Single(snapshot!.Rows);
        Assert.Equal(R1.ToString(), row[0]);
        Assert.Contains("Alice", row!);
    }

    [Fact]
    public void Incremental_rebuild_equals_full_rebuild()
    {
        List<ChangeLogEntry> all = [.. Batch1(), .. Batch2()];

        using TempFolder incrementalDir = new();
        SnapshotBuilder incremental = new(incrementalDir.Path, new CsvRemoteFormat());
        incremental.Rebuild(Table, Columns, Batch1());      // writes sidecar
        incremental.Rebuild(Table, Columns, all);           // seeds from snapshot + folds newer

        using TempFolder fullDir = new();
        SnapshotBuilder full = new(fullDir.Path, new CsvRemoteFormat());
        full.Rebuild(Table, Columns, all);

        Assert.Equal(Dump(full.ReadSnapshot(Table)!), Dump(incremental.ReadSnapshot(Table)!));
    }

    [Fact]
    public void Rebuild_is_atomic_leaving_no_temp_files()
    {
        using TempFolder dir = new();
        SnapshotBuilder builder = new(dir.Path, new CsvRemoteFormat());

        builder.Rebuild(Table, Columns, [.. Batch1(), .. Batch2()]);

        Assert.Empty(Directory.EnumerateFiles(dir.Path, "*.tmp-*"));
        Assert.True(File.Exists(Path.Combine(dir.Path, Table + ".csv")));
    }

    [Fact]
    public void Deleted_rows_are_excluded_from_the_snapshot()
    {
        using TempFolder dir = new();
        SnapshotBuilder builder = new(dir.Path, new CsvRemoteFormat());

        List<ChangeLogEntry> all =
        [
            .. Batch1(),
            .. Batch2(),
            Entry(Table, R2, SyncColumns.IsDeleted, "false", "true", "alice", 5, 3, ChangeOperation.Delete),
        ];
        builder.Rebuild(Table, Columns, all);

        RemoteTable snapshot = builder.ReadSnapshot(Table)!;
        Assert.DoesNotContain(snapshot.Rows, r => r[0] == R2.ToString());
        Assert.Contains(snapshot.Rows, r => r[0] == R1.ToString());
    }

    private static string Dump(RemoteTable table)
        => string.Join("\n", table.Rows.OrderBy(r => r[0]).Select(r => string.Join("|", r)));
}
