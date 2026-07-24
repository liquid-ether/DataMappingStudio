using App.Domain.Catalog;

namespace App.Application.Abstractions;

/// <summary>
/// The column catalog: describes every table's columns and drives the generic store and the dynamic
/// UI editors. Itself one of the folded tables (Architecture §4, §6a).
/// </summary>
public interface ICatalog
{
    /// <summary>All catalog entries across all tables.</summary>
    IReadOnlyList<ColumnCatalogEntry> GetAll();

    /// <summary>Catalog entries for one table, ordered by display order.</summary>
    IReadOnlyList<ColumnCatalogEntry> GetForTable(string table);

    /// <summary>Distinct table names known to the catalog.</summary>
    IReadOnlyList<string> GetTables();

    /// <summary>Seeds catalog entries that are not already present (idempotent provisioning).</summary>
    void Seed(IEnumerable<ColumnCatalogEntry> entries);

    /// <summary>
    /// Adds a user-defined column at runtime: records it in the catalog and applies
    /// <c>ALTER TABLE … ADD COLUMN</c> so it appears automatically in the editors (Architecture §4, §12).
    /// </summary>
    void AddColumn(ColumnCatalogEntry entry);

    /// <summary>
    /// Updates a column's presentation metadata (labels). Never touches structure or the physical
    /// schema; a no-op when the column is unknown.
    /// </summary>
    void UpdateColumnMeta(ColumnCatalogEntry entry);
}
