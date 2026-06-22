using System.Text.Json;
using App.Application;
using App.Application.Abstractions;
using App.Application.Provisioning;
using App.Application.References;
using App.Domain.Data;
using App.Importer;
using App.Infrastructure.Local;
using App.Infrastructure.Remote;
using Microsoft.Extensions.DependencyInjection;

// Excel → SQLite importer + build tooling CLI (Architecture §10/§14). Verbs:
//   provision <remoteFolder>
//   import    <dbPath> <dataDir> <mapping.json> [report.json]
//   rebuild-snapshots <dbPath> <remoteFolder> [format]
//   convert-format <remoteFolder> <fromFormat> <toFormat>
if (args.Length == 0)
{
    Usage();
    return 1;
}

switch (args[0])
{
    case "provision" when args.Length >= 2:
        return Provision(args[1]);
    case "import" when args.Length >= 4:
        return Import(args[1], args[2], args[3], args.Length >= 5 ? args[4] : Path.Combine(args[2], "import-report.json"));
    case "rebuild-snapshots" when args.Length >= 3:
        return RebuildSnapshots(args[1], args[2], args.Length >= 4 ? args[3] : "parquet");
    case "convert-format" when args.Length >= 4:
        return ConvertFormat(args[1], args[2], args[3]);
    case "report" when args.Length >= 3:
        return Report(args[1], args[2], args.Length >= 4 ? args[3] : "parquet");
    case "compact" when args.Length >= 2:
        return Compact(args[1], args.Length >= 3 ? int.Parse(args[2]) : 90, args.Length >= 4 ? args[3] : "parquet");
    default:
        Usage();
        return 1;
}

static int Provision(string remoteFolder)
{
    Directory.CreateDirectory(Path.Combine(remoteFolder, "_changes"));
    Directory.CreateDirectory(Path.Combine(remoteFolder, "_meta"));
    Console.WriteLine($"Provisioned canonical folder at {remoteFolder} (_changes, _meta).");
    return 0;
}

static int Import(string dbPath, string dataDir, string mappingPath, string reportPath)
{
    using ServiceProvider provider = BuildProvider(dbPath, remoteFolder: Path.Combine(dataDir, "_remote"));
    ICatalog catalog = provider.GetRequiredService<ICatalog>();
    catalog.Seed(DefaultCatalog.Entries());
    ILocalStore store = provider.GetRequiredService<ILocalStore>();
    store.EnsureSchema();

    ImportMapping mapping = ImportMapping.FromJson(File.ReadAllText(mappingPath));
    List<WorksheetData> data = mapping.Worksheets.Select(ws => ReadWorksheet(dataDir, ws.Worksheet)).ToList();

    Importer importer = new(catalog, store, provider.GetRequiredService<App.Application.Expressions.RuleExpressionBuilder>());
    ImportReport report = importer.Run(mapping, data, changedBy: "import");

    File.WriteAllText(reportPath, report.ToJson());
    Console.WriteLine($"Import complete: {report.TotalCreated} created, {report.TotalUpdated} updated, {report.TotalSkipped} skipped. Report: {reportPath}");
    return 0;
}

static int RebuildSnapshots(string dbPath, string remoteFolder, string format)
{
    using ServiceProvider provider = BuildProvider(dbPath, remoteFolder, format);
    ICatalog catalog = provider.GetRequiredService<ICatalog>();
    catalog.Seed(DefaultCatalog.Entries());
    IRemoteStore remote = provider.GetRequiredService<IRemoteStore>();
    ISnapshotBuilder snapshots = provider.GetRequiredService<ISnapshotBuilder>();

    IReadOnlyList<ChangeLogEntry> all = remote.ReadAllChanges();
    foreach (string table in catalog.GetTables())
    {
        List<RemoteColumn> columns = catalog.GetForTable(table)
            .Where(e => !e.IsCore && e.ColumnName != SyncColumns.Id)
            .Select(e => new RemoteColumn(e.ColumnName, e.ValueType))
            .ToList();
        snapshots.Rebuild(table, columns, all);
    }

    Console.WriteLine($"Rebuilt snapshots for {catalog.GetTables().Count} tables in {remoteFolder} ({format}).");
    return 0;
}

static int ConvertFormat(string remoteFolder, string from, string to)
{
    using ServiceProvider provider = BuildProvider(dbPath: Path.Combine(remoteFolder, "_convert.db"), remoteFolder, to);
    IRemoteFormatProvider formats = provider.GetRequiredService<IRemoteFormatProvider>();
    IRemoteFormat fromFormat = formats.Resolve(from);
    IRemoteFormat toFormat = formats.Resolve(to);

    string changesDir = Path.Combine(remoteFolder, "_changes");
    int converted = 0;
    if (Directory.Exists(changesDir))
    {
        foreach (string file in Directory.EnumerateFiles(changesDir, $"*{fromFormat.Extension}"))
        {
            string target = Path.ChangeExtension(file, toFormat.Extension.TrimStart('.'));
            toFormat.Write(target, fromFormat.Read(file));
            File.Delete(file);
            converted++;
        }
    }

    Console.WriteLine($"Converted {converted} change-log file(s) from {from} to {to}. Run rebuild-snapshots to refresh snapshots.");
    return 0;
}

static int Report(string dbPath, string remoteFolder, string format)
{
    using ServiceProvider provider = BuildProvider(dbPath, remoteFolder, format);
    ICatalog catalog = provider.GetRequiredService<ICatalog>();
    catalog.Seed(DefaultCatalog.Entries());
    ILocalStore store = provider.GetRequiredService<ILocalStore>();
    store.EnsureSchema();
    IRemoteFormat fmt = provider.GetRequiredService<IRemoteFormatProvider>().Resolve(format);

    ReferenceService references = new(catalog, store);
    ReportingViewBuilder builder = new(remoteFolder, fmt, catalog, store, references, references);
    foreach (string table in catalog.GetTables())
    {
        builder.Build(table);
    }

    Console.WriteLine($"Built reporting views for {catalog.GetTables().Count} tables in {remoteFolder} ({format}).");
    return 0;
}

static int Compact(string remoteFolder, int olderThanDays, string format)
{
    using ServiceProvider provider = BuildProvider(Path.Combine(remoteFolder, "_compact.db"), remoteFolder, format);
    IRemoteFormat fmt = provider.GetRequiredService<IRemoteFormatProvider>().Resolve(format);
    int archived = new LogCompactor(remoteFolder, fmt).Compact(olderThanDays, DateTimeOffset.UtcNow);
    Console.WriteLine($"Compacted: archived {archived} change-log entries older than {olderThanDays} days.");
    return 0;
}

static WorksheetData ReadWorksheet(string dataDir, string worksheet)
{
    string path = Path.Combine(dataDir, worksheet + ".json");
    if (!File.Exists(path))
    {
        return new WorksheetData(worksheet, []);
    }

    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
    List<IReadOnlyDictionary<string, string?>> rows = [];
    foreach (JsonElement element in doc.RootElement.EnumerateArray())
    {
        Dictionary<string, string?> row = new(StringComparer.Ordinal);
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            row[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => prop.Value.GetString(),
                _ => prop.Value.ToString(),
            };
        }

        rows.Add(row);
    }

    return new WorksheetData(worksheet, rows);
}

static ServiceProvider BuildProvider(string dbPath, string remoteFolder, string format = "parquet")
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
    return new ServiceCollection()
        .AddApplication()
        .AddLocalStore(dbPath)
        .AddRemoteStore(remoteFolder, format, enableAutoRefresh: false)
        .BuildServiceProvider();
}

static void Usage() => Console.WriteLine(
    "Usage:\n" +
    "  provision <remoteFolder>\n" +
    "  import <dbPath> <dataDir> <mapping.json> [report.json]\n" +
    "  rebuild-snapshots <dbPath> <remoteFolder> [format]\n" +
    "  convert-format <remoteFolder> <fromFormat> <toFormat>\n" +
    "  report <dbPath> <remoteFolder> [format]\n" +
    "  compact <remoteFolder> [olderThanDays] [format]");
