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

        List<IReadOnlyList<string?>> rows = [];
        foreach (Row row in store.GetAll(table))
        {
            List<string?> cells = [row.Id.ToString()];
            foreach (ColumnCatalogEntry column in columns)
            {
                cells.Add(column.Kind switch
                {
                    ColumnKind.Reference => references.Display(column.ReferenceTarget!, row[column.ColumnName]),
                    ColumnKind.Computed => computed.Evaluate(table, column.ColumnName, row),
                    _ => row[column.ColumnName],
                });
            }

            rows.Add(cells);
        }

        string path = Path.Combine(rootFolder, $"{table}_report{format.Extension}");
        AtomicWrite.Write(path, temp => format.Write(temp, new RemoteTable(reportColumns, rows)));
        return path;
    }
}
