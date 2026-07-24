using App.Domain.Catalog;

namespace App.Application.Sync;

/// <summary>The folded runtime meta-model: user-added tables (with metadata) and columns.</summary>
public sealed record FoldedCatalog(
    IReadOnlyList<TableCatalogEntry> Tables,
    IReadOnlyList<ColumnCatalogEntry> Columns);

/// <summary>
/// Deterministically folds every writer's meta-model change log into the effective runtime catalog
/// (Architecture §6a applied to the meta-model). Entries replay in a stable total order
/// (<c>ChangedAtUtc</c>, then <c>ChangedBy</c>, then <c>ClientSeq</c>); the fold is <b>additive-only</b>
/// — tables and columns are never deleted — with structure resolved first-wins per key and table
/// metadata (labels / navigation / display column) last-writer-wins. Any machine folding the same logs
/// gets the same catalog, with no coordination.
/// </summary>
public static class CatalogFold
{
    public static FoldedCatalog Fold(IReadOnlyList<CatalogChangeEntry> entries)
    {
        Dictionary<string, TableCatalogEntry> tables = new(StringComparer.Ordinal);
        Dictionary<(string Table, string Column), ColumnCatalogEntry> columns = [];

        foreach (CatalogChangeEntry entry in entries
            .OrderBy(e => e.ChangedAtUtc)
            .ThenBy(e => e.ChangedBy, StringComparer.Ordinal)
            .ThenBy(e => e.ClientSeq))
        {
            switch (entry.Kind)
            {
                case CatalogChangeKind.TableAdded when entry.Table is { } added:
                    tables.TryAdd(added.TableName, added); // first-wins: structure is immutable once created
                    break;

                case CatalogChangeKind.TableMetaUpdated when entry.Table is { } meta:
                    tables[meta.TableName] = meta; // last-writer-wins on presentation metadata
                    break;

                case CatalogChangeKind.ColumnAdded when entry.Column is { } column:
                    columns.TryAdd((column.TableName, column.ColumnName), column); // first-wins
                    break;

                case CatalogChangeKind.ColumnMetaUpdated when entry.Column is { } columnMeta:
                    columns[(columnMeta.TableName, columnMeta.ColumnName)] = columnMeta; // last-writer-wins on presentation metadata
                    break;
            }
        }

        return new FoldedCatalog([.. tables.Values], [.. columns.Values]);
    }
}
