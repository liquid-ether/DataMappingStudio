using App.Application.Abstractions;
using App.Application.Expressions;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Domain.Expressions;
using App.Domain.Values;

namespace App.Application.Importing;

/// <summary>Worksheet rows: a list of (header → cell value) maps.</summary>
public sealed record WorksheetData(string Worksheet, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

/// <summary>
/// The Excel→DB import pipeline (Architecture §10): for each worksheet, map only the configured
/// columns (derived/computed columns are intentionally not imported), normalize per the catalog,
/// resolve FK references by natural key, resolve expression field-references to dictionary entries
/// (free-text fallback), then upsert by natural key as one reviewable <see cref="ChangeOperation.Import"/>
/// change set. Bad rows are skipped and reported, never aborting the run. Lives in the Application layer
/// so both the CLI (App.Importer) and the in-app Data Import wizard (App.UI) drive the same engine.
/// </summary>
public sealed class ImportEngine(ICatalog catalog, ILocalStore store, RuleExpressionBuilder expressionBuilder)
{
    public ImportReport Run(ImportMapping mapping, IReadOnlyList<WorksheetData> data, string changedBy)
    {
        ImportReport report = new();
        string changeSetId = Guid.NewGuid().ToString();
        Dictionary<string, Dictionary<string, Guid>> referenceIndexes = new(StringComparer.Ordinal);

        foreach (WorksheetMapping ws in mapping.Worksheets)
        {
            WorksheetReport wsReport = new(ws.Worksheet, ws.Table);
            report.Worksheets.Add(wsReport);

            WorksheetData? sheet = data.FirstOrDefault(d => d.Worksheet == ws.Worksheet);
            if (sheet is null)
            {
                continue;
            }

            Dictionary<string, ColumnCatalogEntry> cols = catalog.GetForTable(ws.Table).ToDictionary(e => e.ColumnName, StringComparer.Ordinal);
            Dictionary<string, Guid> naturalKeyIndex = BuildNaturalKeyIndex(ws.Table, ws.NaturalKey);
            IReadOnlyList<KnownReference> knownRefs = ws.ExpressionColumn is null ? [] : BuildDictionaryReferences();
            HashSet<string> unmapped = new(StringComparer.Ordinal);

            foreach (IReadOnlyDictionary<string, string?> sourceRow in sheet.Rows)
            {
                try
                {
                    ImportRow(ws, sourceRow, cols, naturalKeyIndex, referenceIndexes, knownRefs, unmapped, changeSetId, changedBy, wsReport);
                }
                catch (FormatException ex)
                {
                    wsReport.Skipped++;
                    wsReport.Errors.Add(ex.Message);
                }
            }

            wsReport.UnmappedColumns.AddRange(unmapped.OrderBy(c => c, StringComparer.Ordinal));
        }

        return report;
    }

    private void ImportRow(
        WorksheetMapping ws,
        IReadOnlyDictionary<string, string?> sourceRow,
        Dictionary<string, ColumnCatalogEntry> cols,
        Dictionary<string, Guid> naturalKeyIndex,
        Dictionary<string, Dictionary<string, Guid>> referenceIndexes,
        IReadOnlyList<KnownReference> knownRefs,
        HashSet<string> unmapped,
        string changeSetId,
        string changedBy,
        WorksheetReport report)
    {
        Dictionary<string, string?> values = new(StringComparer.Ordinal);

        foreach ((string header, string? raw) in sourceRow)
        {
            if (!ws.Columns.TryGetValue(header, out string? column))
            {
                unmapped.Add(header); // present in the source but deliberately not imported
                continue;
            }

            if (ws.References.TryGetValue(column, out ColumnReference? reference))
            {
                Guid? resolved = ResolveReference(reference, raw, referenceIndexes);
                if (resolved is not null)
                {
                    report.ResolvedReferences++;
                    values[column] = resolved.Value.ToString();
                }
                else
                {
                    report.UnresolvedReferences++;
                    values[column] = null;
                }
            }
            else if (cols.TryGetValue(column, out ColumnCatalogEntry? entry))
            {
                values[column] = ValueNormalizer.Normalize(entry.ValueType, raw);
            }
        }

        // Required-field check.
        foreach (ColumnCatalogEntry required in cols.Values.Where(c => c.IsRequired))
        {
            if (!values.TryGetValue(required.ColumnName, out string? v) || v is null)
            {
                report.Skipped++;
                report.Errors.Add($"Row skipped: required column '{required.ColumnName}' is empty.");
                return;
            }
        }

        // Expression field-reference resolution (links bare field tokens to dictionary entries).
        if (ws.ExpressionColumn is not null && values.TryGetValue(ws.ExpressionColumn, out string? expr) && expr is not null)
        {
            RuleExpression parsed = expressionBuilder.Build(expr, new ResolutionContext(knownRefs));
            report.ResolvedReferences += parsed.FieldReferences().Count(r => r.IsResolved);
            report.UnresolvedReferences += parsed.UnresolvedReferences().Count();
        }

        // Upsert by natural key: stable Guid across re-runs.
        string nk = NaturalKeyValue(ws.NaturalKey, values);
        bool isUpdate = ws.NaturalKey.Count > 0 && naturalKeyIndex.TryGetValue(nk, out Guid existing);
        Guid id = isUpdate ? naturalKeyIndex[nk] : Guid.NewGuid();

        Row row = new(ws.Table, id);
        foreach ((string column, string? value) in values)
        {
            row[column] = value;
        }

        store.Upsert(ws.Table, row, changeSetId, changedBy, ChangeOperation.Import);

        if (ws.NaturalKey.Count > 0)
        {
            naturalKeyIndex[nk] = id;
        }

        if (isUpdate)
        {
            report.Updated++;
        }
        else
        {
            report.Created++;
        }
    }

    private Guid? ResolveReference(ColumnReference reference, string? rawValue, Dictionary<string, Dictionary<string, Guid>> indexes)
    {
        string? key = ValueNormalizer.Normalize(CatalogValueType.Text, rawValue);
        if (key is null)
        {
            return null;
        }

        string cacheKey = $"{reference.Table}|{reference.By}";
        if (!indexes.TryGetValue(cacheKey, out Dictionary<string, Guid>? index))
        {
            index = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (Row row in store.GetAll(reference.Table, includeDeleted: true))
            {
                if (row[reference.By] is { } v)
                {
                    index[v] = row.Id;
                }
            }

            indexes[cacheKey] = index;
        }

        return index.TryGetValue(key, out Guid id) ? id : null;
    }

    private Dictionary<string, Guid> BuildNaturalKeyIndex(string table, IReadOnlyList<string> naturalKey)
    {
        Dictionary<string, Guid> index = new(StringComparer.Ordinal);
        if (naturalKey.Count == 0)
        {
            return index;
        }

        foreach (Row row in store.GetAll(table, includeDeleted: true))
        {
            index[NaturalKeyValue(naturalKey, row.Values)] = row.Id;
        }

        return index;
    }

    private IReadOnlyList<KnownReference> BuildDictionaryReferences()
        => store.GetAll(Domain.Entities.TableNames.DictionaryEntry, includeDeleted: true)
            .Where(r => r["column_name"] is not null)
            .Select(r => new KnownReference(r["column_name"]!, "self", "·", r.Table, r.Id))
            .ToList();

    private static string NaturalKeyValue(IReadOnlyList<string> naturalKey, IReadOnlyDictionary<string, string?> values)
        => string.Join("", naturalKey.Select(k => values.GetValueOrDefault(k) ?? string.Empty));
}
