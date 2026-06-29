using App.Application.Abstractions;
using App.Application.Expressions;
using App.Application.Importing;
using App.Application.Provisioning;
using App.Domain.Data;
using App.Domain.Entities;
using App.Infrastructure.Local;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace App.Importer.Tests;

/// <summary>
/// End-to-end coverage for the native Excel path: build a real .xlsx in memory, read it with
/// <see cref="ClosedXmlWorkbookReader"/>, and run the importer into a real SQLite working copy — proving
/// the importer targets the local database and can import an Excel file without any external tooling.
/// </summary>
public sealed class ExcelImportTests : IDisposable
{
    private readonly string _dir;
    private readonly LocalDatabase _db;
    private readonly SqliteLocalStore _store;
    private readonly ImportEngine _importer;

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.Zero);
    }

    public ExcelImportTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-xlsx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new LocalDatabase($"Data Source={Path.Combine(_dir, "local.db")}");
        SqliteCatalog catalog = new(_db);
        catalog.Seed(DefaultCatalog.Entries());
        _store = new SqliteLocalStore(_db, catalog, new SqliteAuditLog(_db), new Clock());
        _store.EnsureSchema();

        FunctionLibrary functions = new();
        _importer = new ImportEngine(catalog, _store, new RuleExpressionBuilder(functions, new ExpressionClassifier(functions)));
    }

    private static ImportMapping AppsAndSourceMapping() => new()
    {
        Worksheets =
        [
            new WorksheetMapping
            {
                Worksheet = "Apps",
                Table = TableNames.Application,
                NaturalKey = ["app_code"],
                Columns = new Dictionary<string, string> { ["AppCode"] = "app_code", ["Nom GDM"] = "name_gdm" },
            },
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

    private static MemoryStream BuildWorkbook()
    {
        MemoryStream stream = new();
        using (XLWorkbook workbook = new())
        {
            IXLWorksheet apps = workbook.Worksheets.Add("Apps");
            apps.Cell(1, 1).Value = "AppCode";
            apps.Cell(1, 2).Value = "Nom GDM";
            apps.Cell(1, 3).Value = "Junk";          // present in the sheet but not in the mapping
            apps.Cell(2, 1).Value = "GDM1";
            apps.Cell(2, 2).Value = "Gold DM";
            apps.Cell(2, 3).Value = "ignore me";

            IXLWorksheet source = workbook.Worksheets.Add("Source");
            source.Cell(1, 1).Value = "Nom";
            source.Cell(1, 2).Value = "Code applicatif du système";
            source.Cell(2, 1).Value = "Orders";
            source.Cell(2, 2).Value = "GDM1";        // resolves to the app's id by natural key

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Reads_an_xlsx_and_imports_rows_resolving_references()
    {
        ImportMapping mapping = AppsAndSourceMapping();
        using MemoryStream workbook = BuildWorkbook();

        IReadOnlyList<WorksheetData> data = new ClosedXmlWorkbookReader().Read(workbook, mapping);
        ImportReport report = _importer.Run(mapping, data, "import");

        // Application row landed in the local store with the mapped columns.
        Row app = _store.GetAll(TableNames.Application).Single();
        Assert.Equal("GDM1", app["app_code"]);
        Assert.Equal("Gold DM", app["name_gdm"]);

        // Unmapped header is reported, not imported.
        Assert.Contains("Junk", report.Worksheets.First(w => w.Table == TableNames.Application).UnmappedColumns);

        // Source row resolved its application_id FK to the imported app's id.
        Row source = _store.GetAll(TableNames.DataSource).Single();
        Assert.Equal("Orders", source["name"]);
        Assert.Equal(app.Id.ToString(), source["application_id"]);
        Assert.Equal(2, report.TotalCreated);
    }

    [Fact]
    public void Reader_skips_worksheets_absent_from_the_workbook()
    {
        // The mapping references a "Classification" sheet the workbook does not contain.
        ImportMapping mapping = new()
        {
            Worksheets =
            [
                new WorksheetMapping { Worksheet = "Apps", Table = TableNames.Application, NaturalKey = ["app_code"], Columns = new Dictionary<string, string> { ["AppCode"] = "app_code" } },
                new WorksheetMapping { Worksheet = "Classification", Table = TableNames.Classification, NaturalKey = ["prp"], Columns = new Dictionary<string, string> { ["ClassificationPRP"] = "prp" } },
            ],
        };
        using MemoryStream workbook = BuildWorkbook();

        IReadOnlyList<WorksheetData> data = new ClosedXmlWorkbookReader().Read(workbook, mapping);

        Assert.Equal(2, data.Count);
        Assert.Empty(data.First(d => d.Worksheet == "Classification").Rows); // absent sheet -> empty, no crash
        Assert.Single(data.First(d => d.Worksheet == "Apps").Rows);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
