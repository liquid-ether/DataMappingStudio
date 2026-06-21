using App.Domain.Catalog;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Local.Tests;

/// <summary>
/// Spins up a real SQLite store on a temp-file database with a small seeded catalog (table "widget").
/// Disposed per test so every case starts from a clean database.
/// </summary>
public sealed class LocalStoreFixture : IDisposable
{
    private readonly string _dir;

    public LocalStoreFixture()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        string path = Path.Combine(_dir, "local.db");
        Database = new LocalDatabase($"Data Source={path}");
        Clock = new FakeClock(new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));
        Catalog = new SqliteCatalog(Database);
        Audit = new SqliteAuditLog(Database);
        Store = new SqliteLocalStore(Database, Catalog, Audit, Clock);

        Catalog.Seed(SeedCatalog());
        Store.EnsureSchema();
    }

    public LocalDatabase Database { get; }

    public FakeClock Clock { get; }

    public SqliteCatalog Catalog { get; }

    public SqliteAuditLog Audit { get; }

    public SqliteLocalStore Store { get; }

    public const string Table = "widget";

    public static IEnumerable<ColumnCatalogEntry> SeedCatalog() =>
    [
        new() { TableName = Table, ColumnName = "name", ValueType = CatalogValueType.Text, MaxLength = 100, IsRequired = true, LabelEn = "Name", LabelFr = "Nom", DisplayOrder = 1 },
        new() { TableName = Table, ColumnName = "qty", ValueType = CatalogValueType.Integer, LabelEn = "Quantity", LabelFr = "Quantité", DisplayOrder = 2 },
        new() { TableName = Table, ColumnName = "price", ValueType = CatalogValueType.Number, LabelEn = "Price", LabelFr = "Prix", DisplayOrder = 3 },
        new() { TableName = Table, ColumnName = "active", ValueType = CatalogValueType.Boolean, LabelEn = "Active", LabelFr = "Actif", DisplayOrder = 4 },
    ];

    public void Dispose()
    {
        Database.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; WAL side files can briefly linger on Windows.
        }
    }
}
