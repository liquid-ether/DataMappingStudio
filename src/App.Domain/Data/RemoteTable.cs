using App.Domain.Catalog;

namespace App.Domain.Data;

/// <summary>A typed column in a remote file (per-writer log or materialized snapshot).</summary>
public sealed record RemoteColumn(string Name, CatalogValueType Type);

/// <summary>
/// A simple tabular payload exchanged with the pluggable remote format providers. Values are kept as
/// canonical strings (the same form the local store and fold use); providers convert to/from native
/// types per <see cref="RemoteColumn.Type"/> on write/read so Parquet/Excel stay typed.
/// </summary>
public sealed record RemoteTable(IReadOnlyList<RemoteColumn> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows)
{
    public static RemoteTable Empty(IReadOnlyList<RemoteColumn> columns) => new(columns, []);
}
