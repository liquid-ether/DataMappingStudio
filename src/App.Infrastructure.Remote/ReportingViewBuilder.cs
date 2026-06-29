using App.Application.Abstractions;
using App.Application.References;
using App.Domain.Catalog;
using App.Domain.Data;

namespace App.Infrastructure.Remote;

/// <summary>
/// Builds opt-in denormalized reporting views (Architecture §11): one flat <c>&lt;table&gt;_report.&lt;ext&gt;</c>
/// per table with every reference column resolved to its display value and every computed column
/// evaluated, so a Power Query / Power BI author reads one table with no joins.
/// </summary>
public sealed class ReportingViewBuilder(
    string rootFolder,
    IRemoteFormat format,
    ICatalog catalog,
    ILocalStore store,
    IReferenceResolver references,
    IComputedEvaluator computed)
{
    public string Build(string table)
    {
        List<ColumnCatalogEntry> columns = catalog.GetForTable(table)
            .Where(e => !e.IsCore && e.ColumnName != SyncColumns.Id)
            .OrderBy(e => e.DisplayOrder)
            .ToList();

        List<RemoteColumn> reportColumns = [new RemoteColumn(SyncColumns.Id, CatalogValueType.Uuid)];
        reportColumns.AddRange(columns.Select(c => new RemoteColumn(c.ColumnName, CatalogValueType.Text)));

        List<Row> sourceRows = store.GetAll(table).ToList();

        // Resolve references and computed columns in bulk so each referenced/child table is read once,
        // not once per row. Resolving them per row turned a report over a large table into an O(rows ×
        // table) scan (e.g. data_source's field_count re-scanning every dictionary_entry per source).

        // Reference target -> (row id -> display value), built from one read of each referenced table.
        Dictionary<string, Dictionary<Guid, string>> displayByTarget = columns
            .Where(c => c.Kind == ColumnKind.Reference && c.ReferenceTarget is not null)
            .Select(c => c.ReferenceTarget!)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                target => target,
                target => references.Options(target).ToDictionary(o => o.Id, o => o.Display),
                StringComparer.Ordinal);

        // Computed column -> (row id -> value), each evaluated in a single pass over its child/target table.
        Dictionary<string, IReadOnlyDictionary<Guid, string?>> computedByColumn = columns
            .Where(c => c.Kind == ColumnKind.Computed)
            .ToDictionary(
                c => c.ColumnName,
                c => computed.EvaluateColumn(table, c.ColumnName, sourceRows),
                StringComparer.Ordinal);

        List<IReadOnlyList<string?>> rows = [];
        foreach (Row row in sourceRows)
        {
            List<string?> cells = [row.Id.ToString()];
            foreach (ColumnCatalogEntry column in columns)
            {
                cells.Add(column.Kind switch
                {
                    ColumnKind.Reference => ResolveDisplay(displayByTarget[column.ReferenceTarget!], row[column.ColumnName]),
                    ColumnKind.Computed => computedByColumn[column.ColumnName].GetValueOrDefault(row.Id),
                    _ => row[column.ColumnName],
                });
            }

            rows.Add(cells);
        }

        string path = Path.Combine(rootFolder, $"{table}_report{format.Extension}");
        AtomicWrite.Write(path, temp => format.Write(temp, new RemoteTable(reportColumns, rows)));
        return path;
    }

    // Mirrors IReferenceResolver.Display: the display value for a stored id, or the raw value itself when
    // it is empty / not a guid / points at a missing row.
    private static string? ResolveDisplay(Dictionary<Guid, string> displays, string? id)
        => !string.IsNullOrEmpty(id) && Guid.TryParse(id, out Guid guid) && displays.TryGetValue(guid, out string? display)
            ? display
            : id;
}
