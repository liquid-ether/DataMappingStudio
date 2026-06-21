namespace App.Application.Sync;

/// <summary>Identifies a single field-level cell across the whole store.</summary>
public sealed record CellKey(string Table, Guid RowId, string Column);

/// <summary>The folded current state of one row: its canonical cell values and deleted flag.</summary>
public sealed class FoldedRow(Guid id)
{
    public Guid Id { get; } = id;

    public bool IsDeleted { get; set; }

    public Dictionary<string, string?> Values { get; } = new(StringComparer.Ordinal);
}

/// <summary>The folded current state of one table.</summary>
public sealed class FoldedTable(string name)
{
    public string Name { get; } = name;

    public Dictionary<Guid, FoldedRow> Rows { get; } = [];
}

/// <summary>
/// The deterministic canonical state computed by folding every analyst's change log. Any machine
/// folding the same set of logs gets the same state (Architecture §6a).
/// </summary>
public sealed class FoldedState
{
    public Dictionary<string, FoldedTable> Tables { get; } = new(StringComparer.Ordinal);

    public FoldedTable Table(string name)
    {
        if (!Tables.TryGetValue(name, out FoldedTable? table))
        {
            table = new FoldedTable(name);
            Tables[name] = table;
        }

        return table;
    }

    /// <summary>Canonical value at a cell, or null if absent.</summary>
    public string? Value(CellKey cell)
        => Tables.TryGetValue(cell.Table, out FoldedTable? t) && t.Rows.TryGetValue(cell.RowId, out FoldedRow? r)
            ? r.Values.GetValueOrDefault(cell.Column)
            : null;

    /// <summary>Every non-deleted data cell currently in the state.</summary>
    public IEnumerable<(CellKey Cell, string? Value)> AllCells()
    {
        foreach (FoldedTable table in Tables.Values)
        {
            foreach (FoldedRow row in table.Rows.Values)
            {
                if (row.IsDeleted)
                {
                    continue;
                }

                foreach ((string column, string? value) in row.Values)
                {
                    yield return (new CellKey(table.Name, row.Id, column), value);
                }
            }
        }
    }
}
