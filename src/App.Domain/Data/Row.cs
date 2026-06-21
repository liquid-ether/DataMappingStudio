namespace App.Domain.Data;

/// <summary>
/// A generic, catalog-shaped table row. Values are kept as canonical strings (the form the change
/// log and merge engine compare) keyed by column name, so the metadata-driven UI can bind to any
/// column via the indexer with no compile-time schema (Architecture §4). <c>null</c> means "no value".
/// </summary>
public sealed class Row
{
    private readonly Dictionary<string, string?> _values;

    public Row(string table, Guid id)
    {
        Table = table;
        Id = id;
        _values = new Dictionary<string, string?>(StringComparer.Ordinal);
    }

    public string Table { get; }

    public Guid Id { get; set; }

    /// <summary>Local edit counter (fast unchanged-row skip); not the conflict key (Architecture §6a).</summary>
    public long RowVersion { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>Get/set a column's canonical value. Reading an unknown column yields <c>null</c>.</summary>
    public string? this[string column]
    {
        get => _values.TryGetValue(column, out string? v) ? v : null;
        set => _values[column] = value;
    }

    public bool Has(string column) => _values.ContainsKey(column);

    public IReadOnlyDictionary<string, string?> Values => _values;

    public IEnumerable<string> Columns => _values.Keys;
}
