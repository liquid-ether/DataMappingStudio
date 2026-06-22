using System.Text.RegularExpressions;
using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Domain.Entities;

namespace App.Application.References;

/// <summary>
/// Resolves reference columns (pickers + live display) and evaluates computed columns (count / lookup
/// autofill) over the local store + catalog. Reference display values and computed values are derived
/// on demand, never stored (Architecture §4, Phase 2).
/// </summary>
public sealed partial class ReferenceService(ICatalog catalog, ILocalStore store) : IReferenceResolver, IComputedEvaluator
{
    // The column shown when referencing each table (its natural-key / most identifying column).
    private static readonly Dictionary<string, string> DisplayColumns = new(StringComparer.Ordinal)
    {
        [TableNames.Application] = "app_code",
        [TableNames.DataSource] = "name",
        [TableNames.DictionaryEntry] = "column_name",
        [TableNames.LookupValue] = "value",
        [TableNames.Classification] = "prp",
        [TableNames.Rule] = "name",
    };

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
        if (entry?.Formula is not { } formula)
        {
            return null;
        }

        Match count = CountFormula().Match(formula);
        if (count.Success)
        {
            string childTable = count.Groups["table"].Value;
            string fk = count.Groups["col"].Value;
            return store.GetAll(childTable).Count(r => r[fk] == row.Id.ToString()).ToString();
        }

        Match lookup = LookupFormula().Match(formula);
        if (lookup.Success)
        {
            string refColumn = lookup.Groups["ref"].Value;
            string targetColumn = lookup.Groups["col"].Value;
            ColumnCatalogEntry? refEntry = catalog.GetForTable(table).FirstOrDefault(e => e.ColumnName == refColumn);
            if (refEntry?.ReferenceTarget is { } target && row[refColumn] is { } refId && Guid.TryParse(refId, out Guid guid))
            {
                return store.GetById(target, guid)?[targetColumn];
            }
        }

        return null;
    }

    private string DisplayColumn(string table)
        => DisplayColumns.TryGetValue(table, out string? col) ? col : SyncColumns.Id;

    [GeneratedRegex(@"^\s*count\(\s*(?<table>\w+)\.(?<col>\w+)\s*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CountFormula();

    [GeneratedRegex(@"^\s*lookup\(\s*(?<ref>\w+)\.(?<col>\w+)\s*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LookupFormula();
}
