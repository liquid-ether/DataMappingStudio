using App.Application.Abstractions;
using App.Application.Expressions;
using App.Application.Importing;
using App.Application.Provisioning;
using App.Domain.Data;
using App.Domain.Entities;
using App.Infrastructure.Local;
using Microsoft.Data.Sqlite;

namespace App.Importer.Tests;

public sealed class ImporterTests : IDisposable
{
    private readonly string _dir;
    private readonly LocalDatabase _db;
    private readonly SqliteLocalStore _store;
    private readonly ImportEngine _importer;

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.Zero);
    }

    public ImporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-importer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new LocalDatabase($"Data Source={Path.Combine(_dir, "local.db")}");
        SqliteCatalog catalog = new(_db);
        catalog.Seed(DefaultCatalog.Entries());
        _store = new SqliteLocalStore(_db, catalog, new SqliteAuditLog(_db), new Clock());
        _store.EnsureSchema();

        FunctionLibrary functions = new();
        _importer = new ImportEngine(catalog, _store, new RuleExpressionBuilder(functions, new ExpressionClassifier(functions)));
    }

    private static WorksheetData Sheet(string worksheet, params Dictionary<string, string?>[] rows)
        => new(worksheet, rows.Cast<IReadOnlyDictionary<string, string?>>().ToList());

    private static WorksheetMapping AppsMapping() => new()
    {
        Worksheet = "Apps",
        Table = TableNames.Application,
        NaturalKey = ["app_code"],
        Columns = new Dictionary<string, string> { ["AppCode"] = "app_code", ["Nom GDM"] = "name_gdm" },
    };

    [Fact]
    public void Maps_configured_columns_and_reports_unmapped_ones()
    {
        ImportMapping mapping = new() { Worksheets = [AppsMapping()] };
        WorksheetData apps = Sheet("Apps", new Dictionary<string, string?> { ["AppCode"] = "GDM1", ["Nom GDM"] = "Gold DM", ["Junk"] = "ignore me" });

        ImportReport report = _importer.Run(mapping, [apps], "import");

        Assert.Equal(1, report.TotalCreated);
        Row row = _store.GetAll(TableNames.Application).Single();
        Assert.Equal("GDM1", row["app_code"]);
        Assert.Equal("Gold DM", row["name_gdm"]);
        Assert.Contains("Junk", report.Worksheets.Single().UnmappedColumns);
    }

    [Fact]
    public void Re_run_updates_by_natural_key_without_duplicating()
    {
        ImportMapping mapping = new() { Worksheets = [AppsMapping()] };
        WorksheetData apps = Sheet("Apps", new Dictionary<string, string?> { ["AppCode"] = "GDM1", ["Nom GDM"] = "Gold DM" });

        _importer.Run(mapping, [apps], "import");
        ImportReport second = _importer.Run(mapping, [Sheet("Apps", new Dictionary<string, string?> { ["AppCode"] = "GDM1", ["Nom GDM"] = "Renamed" })], "import");

        Assert.Single(_store.GetAll(TableNames.Application));        // not duplicated
        Assert.Equal(0, second.TotalCreated);
        Assert.Equal(1, second.TotalUpdated);
        Assert.Equal("Renamed", _store.GetAll(TableNames.Application).Single()["name_gdm"]);
    }

    [Fact]
    public void Resolves_foreign_keys_by_natural_key()
    {
        ImportMapping mapping = new()
        {
            Worksheets =
            [
                AppsMapping(),
                new WorksheetMapping
                {
                    Worksheet = "Source",
                    Table = TableNames.DataSource,
                    NaturalKey = ["name"],
                    Columns = new Dictionary<string, string> { ["Nom"] = "name", ["Code applicatif du système"] = "application_id" },
                    References = new Dictionary<string, ColumnReference> { ["application_id"] = new("application", "app_code") },
                },
            ],
        };

        ImportReport report = _importer.Run(mapping,
            [
                Sheet("Apps", new Dictionary<string, string?> { ["AppCode"] = "GDM1", ["Nom GDM"] = "Gold DM" }),
                Sheet("Source", new Dictionary<string, string?> { ["Nom"] = "Orders", ["Code applicatif du système"] = "GDM1" }),
            ], "import");

        Guid appId = _store.GetAll(TableNames.Application).Single().Id;
        Row source = _store.GetAll(TableNames.DataSource).Single();
        Assert.Equal(appId.ToString(), source["application_id"]);
        Assert.True(report.Worksheets.First(w => w.Table == TableNames.DataSource).ResolvedReferences >= 1);
    }

    [Fact]
    public void Unresolved_foreign_key_is_reported_and_left_null()
    {
        ImportMapping mapping = new()
        {
            Worksheets =
            [
                new WorksheetMapping
                {
                    Worksheet = "Source",
                    Table = TableNames.DataSource,
                    NaturalKey = ["name"],
                    Columns = new Dictionary<string, string> { ["Nom"] = "name", ["Code applicatif du système"] = "application_id" },
                    References = new Dictionary<string, ColumnReference> { ["application_id"] = new("application", "app_code") },
                },
            ],
        };

        ImportReport report = _importer.Run(mapping, [Sheet("Source", new Dictionary<string, string?> { ["Nom"] = "Orphan", ["Code applicatif du système"] = "NOPE" })], "import");

        Assert.Null(_store.GetAll(TableNames.DataSource).Single()["application_id"]);
        Assert.True(report.Worksheets.Single().UnresolvedReferences >= 1);
    }

    [Fact]
    public void Rows_missing_a_required_field_are_skipped_not_aborted()
    {
        ImportMapping mapping = new() { Worksheets = [AppsMapping()] };

        ImportReport report = _importer.Run(mapping,
            [Sheet("Apps",
                new Dictionary<string, string?> { ["Nom GDM"] = "No code" },                 // missing required app_code -> skipped
                new Dictionary<string, string?> { ["AppCode"] = "GDM2", ["Nom GDM"] = "Ok" })], // still imported
            "import");

        Assert.Equal(1, report.TotalCreated);
        Assert.Equal(1, report.TotalSkipped);
        Assert.Single(_store.GetAll(TableNames.Application));
    }

    [Fact]
    public void Run_honours_cancellation_and_returns_a_partial_result()
    {
        ImportMapping mapping = new() { Worksheets = [AppsMapping()] };
        using CancellationTokenSource cts = new();
        cts.Cancel(); // already cancelled — the row loop should stop before writing anything

        ImportReport report = _importer.Run(mapping,
            [Sheet("Apps", new Dictionary<string, string?> { ["AppCode"] = "GDM1", ["Nom GDM"] = "X" })],
            "import", cts.Token);

        Assert.Equal(0, report.TotalCreated);
        Assert.Empty(_store.GetAll(TableNames.Application));
    }

    [Fact]
    public void Run_reports_progress_per_row()
    {
        ImportMapping mapping = new() { Worksheets = [AppsMapping()] };
        SyncProgress progress = new();

        _importer.Run(mapping,
            [Sheet("Apps",
                new Dictionary<string, string?> { ["AppCode"] = "A1", ["Nom GDM"] = "x" },
                new Dictionary<string, string?> { ["AppCode"] = "A2", ["Nom GDM"] = "y" })],
            "import", default, progress);

        Assert.Equal([1, 2], progress.Reports); // one report per processed row, cumulative
    }

    private sealed class SyncProgress : IProgress<int>
    {
        public List<int> Reports { get; } = [];
        public void Report(int value) => Reports.Add(value);
    }

    [Fact]
    public void Expression_field_references_resolve_with_free_text_fallback()
    {
        ImportMapping mapping = new()
        {
            Worksheets =
            [
                new WorksheetMapping
                {
                    Worksheet = "Dictionnary",
                    Table = TableNames.DictionaryEntry,
                    NaturalKey = ["column_name"],
                    Columns = new Dictionary<string, string> { ["Colonne"] = "column_name" },
                },
                new WorksheetMapping
                {
                    Worksheet = "Mapping",
                    Table = TableNames.Mapping,
                    NaturalKey = [],
                    ExpressionColumn = "expression",
                    Columns = new Dictionary<string, string> { ["Cible - Règles de transformation"] = "expression" },
                },
            ],
        };

        ImportReport report = _importer.Run(mapping,
            [
                Sheet("Dictionnary", new Dictionary<string, string?> { ["Colonne"] = "lifetime_value" }),
                Sheet("Mapping", new Dictionary<string, string?> { ["Cible - Règles de transformation"] = "lifetime_value + b.unknown" }),
            ], "import");

        WorksheetReport mappingReport = report.Worksheets.First(w => w.Table == TableNames.Mapping);
        Assert.True(mappingReport.ResolvedReferences >= 1);   // lifetime_value -> dictionary entry
        Assert.True(mappingReport.UnresolvedReferences >= 1); // b.unknown -> free-text fallback
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
