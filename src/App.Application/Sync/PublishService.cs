using App.Application.Abstractions;
using App.Domain.Data;

namespace App.Application.Sync;

/// <summary>The result of a publish attempt.</summary>
public sealed record PublishResult(bool Published, int AppendedCount, IReadOnlyList<MergeConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>
/// Publishes an analyst's pending local changes (Architecture §7a): pull the fresh remote fold, run a
/// 3-way field-level merge, and — if there are no unresolved conflicts — append the resulting changes
/// to the analyst's own remote log and rebuild the affected snapshots.
/// </summary>
public interface IPublishService
{
    /// <summary>Computes the merge (clean changes + conflicts) without writing anything.</summary>
    MergeResult Preview(IReadOnlyList<ChangeLogEntry> pendingLocal);

    /// <summary>
    /// Publishes pending local changes. If conflicts remain unresolved by <paramref name="resolutions"/>
    /// nothing is written and the conflicts are returned for the UI.
    /// </summary>
    PublishResult Publish(
        string writerId,
        IReadOnlyList<ChangeLogEntry> pendingLocal,
        IReadOnlyList<ConflictResolution> resolutions);
}

public sealed class PublishService(
    IRemoteStore remoteStore,
    ISnapshotBuilder snapshotBuilder,
    ICatalog catalog,
    FieldMergeEngine mergeEngine,
    IClock clock) : IPublishService
{
    public MergeResult Preview(IReadOnlyList<ChangeLogEntry> pendingLocal)
        => mergeEngine.Merge(CollapseLocal(pendingLocal), ChangeFold.Fold(remoteStore.ReadAllChanges()));

    public PublishResult Publish(
        string writerId,
        IReadOnlyList<ChangeLogEntry> pendingLocal,
        IReadOnlyList<ConflictResolution> resolutions)
    {
        FoldedState remote = ChangeFold.Fold(remoteStore.ReadAllChanges());
        MergeResult merge = mergeEngine.Merge(CollapseLocal(pendingLocal), remote);

        Dictionary<CellKey, ConflictResolution> byCell = resolutions.ToDictionary(r => r.Cell);
        List<MergeConflict> unresolved = merge.Conflicts.Where(c => !byCell.ContainsKey(c.Cell)).ToList();
        if (unresolved.Count > 0)
        {
            return new PublishResult(Published: false, AppendedCount: 0, unresolved);
        }

        List<CellChange> finalChanges =
        [
            .. merge.CleanChanges,
            .. merge.Conflicts.Select(c => new CellChange(c.Cell, mergeEngine.Resolve(c, byCell[c.Cell]))),
        ];

        if (finalChanges.Count == 0)
        {
            return new PublishResult(Published: true, AppendedCount: 0, []);
        }

        IReadOnlyList<ChangeLogEntry> entries = BuildEntries(writerId, finalChanges);
        remoteStore.AppendChanges(writerId, entries);

        IReadOnlyList<ChangeLogEntry> all = remoteStore.ReadAllChanges();
        foreach (string table in finalChanges.Select(c => c.Cell.Table).Distinct())
        {
            snapshotBuilder.Rebuild(table, SnapshotColumns(table), all);
        }

        return new PublishResult(Published: true, entries.Count, []);
    }

    /// <summary>Collapses multiple pending edits of the same cell to a single (base, latest) edit.</summary>
    private static Dictionary<CellKey, LocalEdit> CollapseLocal(IReadOnlyList<ChangeLogEntry> pending)
    {
        Dictionary<CellKey, LocalEdit> result = [];
        foreach (IGrouping<CellKey, ChangeLogEntry> group in pending
            .GroupBy(e => new CellKey(e.Table, e.RowId, e.Column)))
        {
            List<ChangeLogEntry> ordered = group.OrderBy(e => e.ClientSeq).ToList();
            result[group.Key] = new LocalEdit(ordered[0].OldValue, ordered[^1].NewValue);
        }

        return result;
    }

    private IReadOnlyList<ChangeLogEntry> BuildEntries(string writerId, IReadOnlyList<CellChange> changes)
    {
        long seq = remoteStore.ReadWriterChanges(writerId).Select(e => e.ClientSeq).DefaultIfEmpty(0).Max();
        string changeSetId = Guid.NewGuid().ToString();
        DateTimeOffset now = clock.UtcNow;

        return changes.Select(change => new ChangeLogEntry
        {
            ChangeId = Guid.NewGuid(),
            ChangeSetId = changeSetId,
            Table = change.Cell.Table,
            RowId = change.Cell.RowId,
            Column = change.Cell.Column,
            OldValue = null,
            NewValue = change.Value,
            Operation = ChangeOperation.Update,
            ChangedBy = writerId,
            ChangedAtUtc = now,
            ClientSeq = ++seq,
        }).ToList();
    }

    private IReadOnlyList<RemoteColumn> SnapshotColumns(string table)
        => catalog.GetForTable(table)
            .Where(e => !e.IsCore && e.ColumnName != SyncColumns.Id)
            .Select(e => new RemoteColumn(e.ColumnName, e.ValueType))
            .ToList();
}
