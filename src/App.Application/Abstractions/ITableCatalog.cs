using App.Domain.Catalog;

namespace App.Application.Abstractions;

/// <summary>
/// Table-level metadata (labels, navigation placement, reference display column) beside the column
/// catalog. A separate interface from <see cref="ICatalog"/> so existing consumers/test doubles of the
/// column catalog are untouched; the same SQLite-backed implementation provides both.
/// </summary>
public interface ITableCatalog
{
    IReadOnlyList<TableCatalogEntry> GetTableMeta();

    TableCatalogEntry? GetTableMeta(string table);

    /// <summary>Inserts or fully updates a table's metadata (last write wins).</summary>
    void UpsertTableMeta(TableCatalogEntry entry);

    /// <summary>Idempotently inserts metadata that isn't present yet (defaults seeding).</summary>
    void SeedTableMeta(IEnumerable<TableCatalogEntry> entries);
}
