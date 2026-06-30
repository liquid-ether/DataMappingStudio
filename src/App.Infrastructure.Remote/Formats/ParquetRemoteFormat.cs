using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;
using Parquet;
using Parquet.Schema;
using Parquet.Serialization;

namespace App.Infrastructure.Remote.Formats;

/// <summary>
/// Parquet format (default) via Parquet.Net — typed, columnar, compact; types travel natively into
/// Power BI/Excel (Architecture §6a/§7). Uses Parquet.Net's untyped (dictionary-per-row) serializer;
/// values convert between canonical strings and native typed columns. Parquet.Net is async, so the
/// synchronous <see cref="IRemoteFormat"/> members block on the async core. That core is dispatched via
/// <see cref="Task.Run{TResult}(Func{Task{TResult}})"/> so it runs on a thread-pool thread with no
/// captured <see cref="SynchronizationContext"/>: blocking it from a single-threaded context (the Blazor
/// Server circuit) would otherwise deadlock when Parquet.Net's internal continuations post back to that
/// context.
/// </summary>
public sealed class ParquetRemoteFormat : IRemoteFormat
{
    public string Name => "parquet";

    public string Extension => ".parquet";

    public void Write(string path, RemoteTable table) => Task.Run(() => WriteAsync(path, table)).GetAwaiter().GetResult();

    public RemoteTable Read(string path) => Task.Run(() => ReadAsync(path)).GetAwaiter().GetResult();

    private static async Task WriteAsync(string path, RemoteTable table)
    {
        DataField[] fields = table.Columns.Select(ToDataField).ToArray();
        ParquetSchema schema = new(fields);

        List<IDictionary<string, object?>> rows = new(table.Rows.Count);
        foreach (IReadOnlyList<string?> row in table.Rows)
        {
            Dictionary<string, object?> dict = new(table.Columns.Count);
            for (int c = 0; c < table.Columns.Count; c++)
            {
                object? native = RemoteValueConverter.ToNative(table.Columns[c].Type, c < row.Count ? row[c] : null);
                if (native is not null)
                {
                    dict[table.Columns[c].Name] = native; // omitted keys deserialize back to null
                }
            }

            rows.Add(dict);
        }

        await using FileStream stream = File.Create(path);
        await ParquetSerializer.SerializeUntypedAsync(rows, schema, stream);
    }

    private static async Task<RemoteTable> ReadAsync(string path)
    {
        Parquet.Serialization.DeserializationResult<Dictionary<string, object>> result;
        await using (FileStream stream = File.OpenRead(path))
        {
            result = await ParquetSerializer.DeserializeUntypedAsync(stream);
        }

        List<RemoteColumn> columns = result.Schema.GetDataFields()
            .Select(f => new RemoteColumn(f.Name, FromClrType(f.ClrType)))
            .ToList();

        List<IReadOnlyList<string?>> rows = new(result.Data.Count);
        foreach (Dictionary<string, object> record in result.Data)
        {
            List<string?> row = new(columns.Count);
            for (int c = 0; c < columns.Count; c++)
            {
                object? native = record.GetValueOrDefault(columns[c].Name);
                row.Add(RemoteValueConverter.ToCanonical(columns[c].Type, native));
            }

            rows.Add(row);
        }

        return new RemoteTable(columns, rows);
    }

    private static DataField ToDataField(RemoteColumn column) => column.Type switch
    {
        CatalogValueType.Integer => new DataField<long?>(column.Name),
        CatalogValueType.Number => new DataField<double?>(column.Name),
        CatalogValueType.Boolean => new DataField<bool?>(column.Name),
        _ => new DataField<string>(column.Name),
    };

    private static CatalogValueType FromClrType(Type clr)
    {
        if (clr == typeof(long)) { return CatalogValueType.Integer; }
        if (clr == typeof(double)) { return CatalogValueType.Number; }
        if (clr == typeof(bool)) { return CatalogValueType.Boolean; }
        return CatalogValueType.Text;
    }
}
