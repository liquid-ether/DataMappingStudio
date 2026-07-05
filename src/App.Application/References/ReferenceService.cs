using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Domain.Entities;

namespace App.Application.References;

/// <summary>
/// Resolves reference columns (pickers + live display) and evaluates computed columns
/// (<see cref="ComputedFormula"/> autofill) over the local store + catalog. Reference display values and
/// computed values are derived on demand, never stored (Architecture §4, Phase 2). The display column per
/// table comes from the table catalog when present (so runtime-created tables render sensibly in
/// pickers), falling back to the built-in map for the default entities, then the row id.
/// </summary>
public sealed class ReferenceService(ICatalog catalog, ILocalStore store, ITableCatalog? tables = null) : IReferenceResolver, IComputedEvaluator
{
    // Built-in fallback: the column shown when referencing each default table (its natural key).
    private static readonly Dictionary<string, string> DisplayColumns = new(StringComparer.Ordinal)
    {
        [TableNames.Application] = "app_code",
        [TableNames.DataSource] = "name",
        [TableNames.DictionaryEntry] = "column_name",
        [TableNames.LookupValue] = "value",
        [TableNames.Classification] = "prp",
        [TableNames.Rule] = "name",
    };

    /// <summary>Whether a built-in display column exists for the table (used by formula validation).</summary>
    public static bool HasDefaultDisplayColumn(string table) => DisplayColumns.ContainsKey(table);

    public IReadOnlyList<ReferenceOption> Options(string targetTable)
    {
        string display = DisplayColumn(targetTable);
        return store.GetAll(targetTable)
            .Select(r => new ReferenceOption(r.Id, r[display] ?? r.Id.ToString()))
            .OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? Display(string targetTable, string? id)
    {
        if (string.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid guid))
        {
            return id;
        }

        Row? row = store.GetById(targetTable, guid);
        return row?[DisplayColumn(targetTable)] ?? id;
    }

    public string? Evaluate(string table, string column, Row row)
    {
        ColumnCatalogEntry? entry = catalog.GetForTable(table).FirstOrDefault(e => e.ColumnName == column);
        switch (ComputedFormula.Parse(entry?.Formula))
        {
            case ComputedFormula.Count(var childTable, var fk):
                return store.GetAll(childTable).Count(r => r[fk] == row.Id.ToString()).ToString();

            case ComputedFormula.RefLookup(var refColumn, var targetColumn):
                ColumnCatalogEntry? refEntry = catalog.GetForTable(table).FirstOrDefault(e => e.ColumnName == refColumn);
                if (refEntry?.ReferenceTarget is { } target && row[refColumn] is { } refId && Guid.TryParse(refId, out Guid guid))
                {
                    return store.GetById(target, guid)?[targetColumn];
                }

                return null;

            case ComputedFormula.KeyLookup(var keyColumn, var targetTable, var matchColumn, var returnColumn):
                string match = matchColumn ?? DisplayColumn(targetTable);
                if (row[keyColumn] is not { Length: > 0 } key)
                {
                    return null;
                }

                return store.GetAll(targetTable).FirstOrDefault(t => t[match] == key)?[returnColumn];

            default:
                return null;
        }
    }

    public IReadOnlyDictionary<Guid, string?> EvaluateColumn(string table, string column, IReadOnlyList<Row> rows)
    {
        Dictionary<Guid, string?> result = new(rows.Count);
        IReadOnlyList<ColumnCatalogEntry> entries = catalog.GetForTable(table);
        ColumnCatalogEntry? entry = entries.FirstOrDefault(e => e.ColumnName == column);

        switch (ComputedFormula.Parse(entry?.Formula))
        {
            case ComputedFormula.Count(var childTable, var fk):
            {
                // Read the child table once and tally references by FK value, instead of re-scanning it
                // for every parent row (the source of the grid's scroll lag).
                Dictionary<string, int> counts = new(StringComparer.Ordinal);
                foreach (Row child in store.GetAll(childTable))
                {
                    if (child[fk] is { } key)
                    {
                        counts[key] = counts.GetValueOrDefault(key) + 1;
                    }
                }

                foreach (Row row in rows)
                {
                    result[row.Id] = counts.GetValueOrDefault(row.Id.ToString()).ToString();
                }

                return result;
            }

            case ComputedFormula.RefLookup(var refColumn, var targetColumn)
                when entries.FirstOrDefault(e => e.ColumnName == refColumn)?.ReferenceTarget is { } target:
            {
                // Index the target table once, then resolve each row's referenced value from memory.
                Dictionary<Guid, Row> byId = [];
                foreach (Row t in store.GetAll(target))
                {
                    byId[t.Id] = t;
                }

                foreach (Row row in rows)
                {
                    result[row.Id] = row[refColumn] is { } refId && Guid.TryParse(refId, out Guid guid) && byId.TryGetValue(guid, out Row? targetRow)
                        ? targetRow[targetColumn]
                        : null;
                }

                return result;
            }

            case ComputedFormula.KeyLookup(var keyColumn, var targetTable, var matchColumn, var returnColumn):
            {
                // Index the target table by match value once (first row wins on duplicates).
                string match = matchColumn ?? DisplayColumn(targetTable);
                Dictionary<string, string?> byKey = new(StringComparer.Ordinal);
                foreach (Row t in store.GetAll(targetTable))
                {
                    if (t[match] is { Length: > 0 } key)
                    {
                        byKey.TryAdd(key, t[returnColumn]);
                    }
                }

                foreach (Row row in rows)
                {
                    result[row.Id] = row[keyColumn] is { Length: > 0 } key && byKey.TryGetValue(key, out string? value)
                        ? value
                        : null;
                }

                return result;
            }
        }

        foreach (Row row in rows)
        {
            result[row.Id] = null;
        }

        return result;
    }

    private string DisplayColumn(string table)
        => tables?.GetTableMeta(table)?.DisplayColumn
            ?? (DisplayColumns.TryGetValue(table, out string? col) ? col : SyncColumns.Id);
}
