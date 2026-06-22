using App.Application.References;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Domain.Entities;
using App.Infrastructure.Local;
using App.Infrastructure.Remote.Formats;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Remote.Tests;

public sealed class ReportingAndCompactionTests : IDisposable
{
    private readonly string _dir;
    private readonly LocalDatabase _db;

    public ReportingAndCompactionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new LocalDatabase($"Data Source={Path.Combine(_dir, "local.db")}");
    }

    [Fact]
    public void Reporting_view_resolves_references_and_evaluates_computed_columns()
    {
        SqliteCatalog catalog = new(_db);
        catalog.Seed(App.Application.Provisioning.DefaultCatalog.Entries());
        SqliteLocalStore store = new(_db, catalog, new SqliteAuditLog(_db), new FixedClock(RemoteTestData.T0));
        store.EnsureSchema();

        Guid appId = Guid.NewGuid();
        Guid sourceId = Guid.NewGuid();
        store.Upsert(TableNames.Application, new Row(TableNames.Application, appId) { ["app_code"] = "GDM1" }, "s", "t");
        store.Upsert(TableNames.DataSource, new Row(TableNames.DataSource, sourceId) { ["name"] = "Orders", ["application_id"] = appId.ToString() }, "s", "t");
        store.Upsert(TableNames.DictionaryEntry, new Row(TableNames.DictionaryEntry, Guid.NewGuid()) { ["column_name"] = "c1", ["source_id"] = sourceId.ToString() }, "s", "t");
        store.Upsert(TableNames.DictionaryEntry, new Row(TableNames.DictionaryEntry, Guid.NewGuid()) { ["column_name"] = "c2", ["source_id"] = sourceId.ToString() }, "s", "t");

        ReferenceService refs = new(catalog, store);
        CsvRemoteFormat format = new();
        ReportingViewBuilder builder = new(_dir, format, catalog, store, refs, refs);

        builder.Build(TableNames.DataSource);

        RemoteTable view = format.Read(Path.Combine(_dir, "data_source_report.csv"));
        int appCol = view.Columns.ToList().FindIndex(c => c.Name == "application_id");
        int countCol = view.Columns.ToList().FindIndex(c => c.Name == "field_count");
        IReadOnlyList<string?> row = view.Rows.Single(r => r[0] == sourceId.ToString());

        Assert.Equal("GDM1", row[appCol]);   // reference resolved to display
        Assert.Equal("2", row[countCol]);     // computed count of dictionary entries
    }

    [Fact]
    public void Compaction_archives_old_entries_and_keeps_recent_ones()
    {
        CsvRemoteFormat format = new();
        FileRemoteStore remote = new(_dir, format);
        DateTimeOffset now = new(2026, 6, 22, 0, 0, 0, TimeSpan.Zero);
        Guid row = Guid.NewGuid();

        remote.AppendChanges("alice",
        [
            Aged(row, "old", "alice", 1, now.AddDays(-100)),
            Aged(row, "recent", "alice", 2, now.AddDays(-1)),
        ]);

        int archived = new LogCompactor(_dir, format).Compact(olderThanDays: 90, now);

        Assert.Equal(1, archived);
        Assert.Equal("recent", remote.ReadWriterChanges("alice").Single().NewValue);
        Assert.True(File.Exists(Path.Combine(_dir, "_changes", "archive", "alice.csv")));
    }

    private static ChangeLogEntry Aged(Guid row, string value, string by, long seq, DateTimeOffset at) => new()
    {
        ChangeId = Guid.NewGuid(),
        ChangeSetId = "cs",
        Table = "widget",
        RowId = row,
        Column = "name",
        NewValue = value,
        Operation = ChangeOperation.Update,
        ChangedBy = by,
        ChangedAtUtc = at,
        ClientSeq = seq,
    };

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
