namespace App.Application.Sync;

/// <summary>A field-level conflict: the analyst's base, their local edit, and the diverging remote value.</summary>
public sealed record MergeConflict(CellKey Cell, string? Base, string? Local, string? Remote);

/// <summary>The outcome of a 3-way merge: changes safe to publish, plus conflicts needing resolution.</summary>
public sealed record MergeResult(IReadOnlyList<CellChange> CleanChanges, IReadOnlyList<MergeConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>A resolved cell value destined for the analyst's log.</summary>
public sealed record CellChange(CellKey Cell, string? Value);

/// <summary>How an analyst resolved a conflict (Architecture §7a).</summary>
public enum ConflictChoice
{
    KeepMine,
    KeepTheirs,
    Edit,
}

public sealed record ConflictResolution(CellKey Cell, ConflictChoice Choice, string? EditValue = null);

/// <summary>
/// Generic field-level 3-way merge (Architecture §7a). For each locally-changed cell it compares the
/// analyst's BASE (value when the edit started) and the fresh REMOTE fold: remote unchanged → publish
/// the local edit; remote converged to the same value → no-op; otherwise a conflict to resolve as
/// keep-mine / keep-theirs / edit.
/// </summary>
public sealed class FieldMergeEngine
{
    public MergeResult Merge(IReadOnlyDictionary<CellKey, LocalEdit> localChanges, FoldedState remote)
    {
        List<CellChange> clean = [];
        List<MergeConflict> conflicts = [];

        foreach ((CellKey cell, LocalEdit edit) in localChanges)
        {
            string? remoteValue = remote.Value(cell);

            if (string.Equals(remoteValue, edit.Base, StringComparison.Ordinal))
            {
                clean.Add(new CellChange(cell, edit.Local));
            }
            else if (!string.Equals(remoteValue, edit.Local, StringComparison.Ordinal))
            {
                conflicts.Add(new MergeConflict(cell, edit.Base, edit.Local, remoteValue));
            }

            // remote == local: already converged, nothing to publish.
        }

        return new MergeResult(clean, conflicts);
    }

    /// <summary>The value to publish for a resolved conflict.</summary>
    public string? Resolve(MergeConflict conflict, ConflictResolution resolution) => resolution.Choice switch
    {
        ConflictChoice.KeepMine => conflict.Local,
        ConflictChoice.KeepTheirs => conflict.Remote,
        ConflictChoice.Edit => resolution.EditValue,
        _ => conflict.Local,
    };
}

/// <summary>A collapsed local edit for a cell: the base value when editing started and the latest local value.</summary>
public sealed record LocalEdit(string? Base, string? Local);
