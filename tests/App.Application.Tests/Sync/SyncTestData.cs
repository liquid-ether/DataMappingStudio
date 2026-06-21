using App.Application.Sync;
using App.Domain.Data;

namespace App.Application.Tests.Sync;

/// <summary>Helpers for building change-log entries and inspecting folded state in sync tests.</summary>
public static class SyncTestData
{
    public static readonly DateTimeOffset T0 = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    public static ChangeLogEntry Entry(
        string table, Guid row, string column, string? oldValue, string? newValue,
        string by, long seq, int minute, ChangeOperation op = ChangeOperation.Update)
        => new()
        {
            ChangeId = Guid.NewGuid(),
            ChangeSetId = $"{by}-cs",
            Table = table,
            RowId = row,
            Column = column,
            OldValue = oldValue,
            NewValue = newValue,
            Operation = op,
            ChangedBy = by,
            ChangedAtUtc = T0.AddMinutes(minute),
            ClientSeq = seq,
        };

    /// <summary>Flattens folded state to a comparable cell map (deleted rows excluded).</summary>
    public static SortedDictionary<string, string?> Cells(FoldedState state)
    {
        SortedDictionary<string, string?> map = new(StringComparer.Ordinal);
        foreach ((CellKey cell, string? value) in state.AllCells())
        {
            map[$"{cell.Table}|{cell.RowId}|{cell.Column}"] = value;
        }

        return map;
    }
}
