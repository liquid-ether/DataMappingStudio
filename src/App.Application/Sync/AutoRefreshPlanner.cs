namespace App.Application.Sync;

/// <summary>What an auto-refresh would do: cells to fast-forward locally, and cells to flag for review.</summary>
public sealed record RefreshPlan(IReadOnlyList<CellChange> Applied, IReadOnlyList<CellKey> Flagged);

/// <summary>
/// Computes a non-disruptive auto-refresh (Architecture §9): fast-forward remote changes for cells the
/// analyst has not locally edited; cells edited locally <em>and</em> changed remotely are flagged for
/// review at next publish, never force-resolved — so unpublished local edits are never clobbered.
/// </summary>
public sealed class AutoRefreshPlanner
{
    public RefreshPlan Plan(IReadOnlySet<CellKey> locallyEditedCells, FoldedState localKnown, FoldedState remote)
    {
        List<CellChange> applied = [];
        List<CellKey> flagged = [];

        foreach ((CellKey cell, string? remoteValue) in remote.AllCells())
        {
            if (string.Equals(remoteValue, localKnown.Value(cell), StringComparison.Ordinal))
            {
                continue; // unchanged remotely
            }

            if (locallyEditedCells.Contains(cell))
            {
                flagged.Add(cell);
            }
            else
            {
                applied.Add(new CellChange(cell, remoteValue));
            }
        }

        return new RefreshPlan(applied, flagged);
    }
}
