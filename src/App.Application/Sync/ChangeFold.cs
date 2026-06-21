using App.Domain.Data;

namespace App.Application.Sync;

/// <summary>
/// The deterministic fold (Architecture §6a/§8): replays change-log entries in a stable order
/// (by <c>ChangedAtUtc</c>, then <c>ChangeId</c> as tiebreak) applying last-write-per-cell, so the
/// canonical state is independent of the order logs are read in. The ordered entries are also the
/// canonical <c>audit_log</c>.
/// </summary>
public static class ChangeFold
{
    public static IReadOnlyList<ChangeLogEntry> Order(IEnumerable<ChangeLogEntry> entries)
        => entries.OrderBy(e => e.ChangedAtUtc).ThenBy(e => e.ChangeId).ToList();

    public static FoldedState Fold(IEnumerable<ChangeLogEntry> entries) => Apply(new FoldedState(), entries);

    /// <summary>Folds <paramref name="entries"/> on top of an existing <paramref name="seed"/> state (incremental refold).</summary>
    public static FoldedState Apply(FoldedState seed, IEnumerable<ChangeLogEntry> entries)
    {
        foreach (ChangeLogEntry e in Order(entries))
        {
            FoldedTable table = seed.Table(e.Table);
            if (!table.Rows.TryGetValue(e.RowId, out FoldedRow? row))
            {
                row = new FoldedRow(e.RowId);
                table.Rows[e.RowId] = row;
            }

            row.Values[e.Column] = e.NewValue;
            if (e.Column == SyncColumns.IsDeleted)
            {
                row.IsDeleted = e.NewValue == "true";
            }
        }

        return seed;
    }
}
