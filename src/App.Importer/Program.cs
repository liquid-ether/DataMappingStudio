using App.Application;
using App.Application.Abstractions;
using App.Application.Importing;
using App.Application.Provisioning;
using App.Application.References;
using App.Domain.Data;
using App.Importer;
using App.Infrastructure.Local;
using App.Infrastructure.Remote;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Excel → SQLite importer + build tooling CLI (Architecture §10/§14). Verbs:
//   provision <remoteFolder>
//   import-excel <workbook.xlsx> <mappingName> [dbPath] [report.json]
//   list-mappings [remoteFolder]
//   delete-mapping <mappingName> [remoteFolder]
//   rebuild-snapshots <dbPath> <remoteFolder> [format]
//   convert-format <remoteFolder> <fromFormat> <toFormat>
// Imports run ONLY mappings previously saved from the app's Data Import wizard (shared folder,
// _meta/import-mappings) — the wizard is where mappings are authored and validated.
if (args.Length == 0)
{
    Usage();
    return 1;
}

switch (args[0])
{
    case "provision" when args.Length >= 2:
        return Provision(args[1]);
    case "import-excel" when args.Length >= 3:
        return ImportExcel(args[1], args[2], Arg(args, 3), Arg(args, 4));
    case "list-mappings":
        return ListMappings(Arg(args, 1));
    case "delete-mapping" when args.Length >= 2:
        return DeleteMapping(args[1], Arg(args, 2));
    case "rebuild-snapshots" when args.Length >= 3:
        return RebuildSnapshots(args[1], args[2], args.Length >= 4 ? args[3] : "parquet");
    case "convert-format" when args.Length >= 4:
        return ConvertFormat(args[1], args[2], args[3]);
    case "report" when args.Length >= 3:
        return Report(args[1], args[2], args.Length >= 4 ? args[3] : "parquet");
    case "compact" when args.Length >= 2:
        return Compact(args[1], args.Length >= 3 ? int.Parse(args[2]) : 90, args.Length >= 4 ? args[3] : "parquet");
    case "seed-sample" when args.Length >= 2:
        return SeedSample(args[1]);
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

// Reads a real .xlsx directly (no PowerShell/ImportExcel needed) and imports it into the local SQLite
// working copy, using a mapping previously saved from the app's Data Import wizard — the CLI never
// defines mappings itself. The db path defaults from appsettings.json (or DMS_Importer__* env vars).
static int ImportExcel(string workbookPath, string mappingName, string? dbArg, string? reportArg)
{
    ImporterConfig config = LoadConfig();
    string dbPath = config.ResolveDbPath(dbArg);
    string remoteFolder = config.ResolveRemoteFolder(null, dbPath);

    if (!File.Exists(workbookPath))
    {
        Console.Error.WriteLine($"Workbook not found: {workbookPath}");
        return 1;
    }

    string reportPath = reportArg ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "import-report.json");

    using ServiceProvider provider = BuildProvider(dbPath, remoteFolder);
    IImportMappingStore mappings = provider.GetRequiredService<IImportMappingStore>();
    ImportMapping? mapping = mappings.Get(mappingName);
    if (mapping is null)
    {
        Console.Error.WriteLine($"Mapping '{mappingName}' not found in {remoteFolder}.");
        Console.Error.WriteLine("Save a mapping from the app's Data Import wizard first. Available mappings:");
        foreach (SavedMappingInfo info in mappings.List())
        {
            Console.Error.WriteLine($"  {info.Name}  (saved by {info.SavedBy}, {info.SavedAtUtc:yyyy-MM-dd HH:mm} UTC)");
        }

        return 1;
    }

    ICatalog catalog = provider.GetRequiredService<ICatalog>();
    catalog.Seed(DefaultCatalog.Entries());
    ILocalStore store = provider.GetRequiredService<ILocalStore>();
    store.EnsureSchema();

    using FileStream workbookStream = File.OpenRead(workbookPath);
    IReadOnlyList<WorksheetData> data = provider.GetRequiredService<IWorkbookReader>().Read(workbookStream, mapping);

    ImportReport report = provider.GetRequiredService<ImportEngine>().Run(mapping, data, config.ChangedBy);

    File.WriteAllText(reportPath, report.ToJson());
    Console.WriteLine($"Imported '{Path.GetFileName(workbookPath)}' into {dbPath} using mapping '{mappingName}': {report.TotalCreated} created, {report.TotalUpdated} updated, {report.TotalSkipped} skipped. Report: {reportPath}");
    return 0;
}

// Lists the mappings saved from the app's Data Import wizard (the only ones import-excel can run).
static int ListMappings(string? remoteArg)
{
    ImporterConfig config = LoadConfig();
    string remoteFolder = config.ResolveRemoteFolder(remoteArg, config.ResolveDbPath(null));
    IReadOnlyList<SavedMappingInfo> saved = new FileImportMappingStore(remoteFolder).List();
    if (saved.Count == 0)
    {
        Console.WriteLine($"No saved mappings in {remoteFolder}. Save one from the app's Data Import wizard (Mapping step).");
        return 0;
    }

    Console.WriteLine($"Saved mappings in {remoteFolder}:");
    foreach (SavedMappingInfo info in saved)
    {
        Console.WriteLine($"  {info.Name}  (saved by {info.SavedBy}, {info.SavedAtUtc:yyyy-MM-dd HH:mm} UTC)");
    }

    return 0;
}

// Deletes a saved mapping from the shared folder (rename = save under a new name in the wizard, then delete).
static int DeleteMapping(string name, string? remoteArg)
{
    ImporterConfig config = LoadConfig();
    string remoteFolder = config.ResolveRemoteFolder(remoteArg, config.ResolveDbPath(null));
    if (new FileImportMappingStore(remoteFolder).Delete(name))
    {
        Console.WriteLine($"Deleted mapping '{name}' from {remoteFolder}.");
        return 0;
    }

    Console.Error.WriteLine($"Mapping '{name}' not found in {remoteFolder}.");
    return 1;
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

static int SeedSample(string dbPath)
{
    using ServiceProvider provider = BuildProvider(dbPath, remoteFolder: Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "_remote"));
    provider.GetRequiredService<ICatalog>().Seed(DefaultCatalog.Entries());
    provider.GetRequiredService<ILocalStore>().EnsureSchema();

    int seeded = new SampleDataSeeder(provider.GetRequiredService<LocalDatabase>()).SeedIfEmpty();
    Console.WriteLine(seeded == 0
        ? $"Store at {dbPath} already has data; nothing seeded."
        : $"Seeded {seeded} sample rows into {dbPath} (10 applications, 100 sources, 5000 dictionary entries, 30 mapping targets).");
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

static ServiceProvider BuildProvider(string dbPath, string remoteFolder, string format = "parquet")
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
    return new ServiceCollection()
        .AddApplication()
        .AddLocalStore(dbPath)
        .AddRemoteStore(remoteFolder, format, enableAutoRefresh: false)
        .BuildServiceProvider();
}

// Loads appsettings.json (next to the exe) + DMS_-prefixed environment variables into the Importer config.
static ImporterConfig LoadConfig()
{
    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables(prefix: "DMS_")
        .Build();
    return configuration.GetSection("Importer").Get<ImporterConfig>() ?? new ImporterConfig();
}

// Optional positional argument at index i, or null when absent.
static string? Arg(string[] args, int i) => i < args.Length ? args[i] : null;

static void Usage() => Console.WriteLine(
    "Usage:\n" +
    "  provision <remoteFolder>\n" +
    "  import-excel <workbook.xlsx> <mappingName> [dbPath] [report.json]   (mapping = saved from the app's Data Import wizard)\n" +
    "  list-mappings [remoteFolder]\n" +
    "  delete-mapping <mappingName> [remoteFolder]\n" +
    "  rebuild-snapshots <dbPath> <remoteFolder> [format]\n" +
    "  convert-format <remoteFolder> <fromFormat> <toFormat>\n" +
    "  report <dbPath> <remoteFolder> [format]\n" +
    "  compact <remoteFolder> [olderThanDays] [format]\n" +
    "  seed-sample <dbPath>");
