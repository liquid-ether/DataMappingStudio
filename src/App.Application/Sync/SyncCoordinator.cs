using App.Application.Abstractions;
using App.Domain.Data;

namespace App.Application.Sync;

public sealed record RefreshResult(int Applied, int Flagged);

/// <summary>
/// Coordinates publish / preview / auto-refresh / history for the UI, tracking the analyst's
/// last-published sequence so "pending" means local change-log entries newer than that.
/// </summary>
public interface ISyncCoordinator
{
    string WriterId { get; }

    int PendingCount();

    /// <summary>3-way merge of pending local edits against the fresh remote fold (no writes).</summary>
    MergeResult Preview();

    /// <summary>Publishes pending edits; blocked (and returns conflicts) if any are unresolved.</summary>
    PublishResult Publish(IReadOnlyList<ConflictResolution> resolutions);

    /// <summary>Fast-forwards untouched cells from the remote fold into the local store.</summary>
    RefreshResult Refresh();

    /// <summary>The audit trail (folded change log), optionally filtered.</summary>
    IReadOnlyList<ChangeLogEntry> History(string? table = null, Guid? rowId = null);
}

public sealed class SyncCoordinator(
    ICatalog catalog,
    ILocalStore localStore,
    IAuditLog audit,
    IRemoteStore remoteStore,
    IPublishService publishService,
    AutoRefreshPlanner planner) : ISyncCoordinator
{
    private readonly object _gate = new();
    private long _lastPublishedSeq;

    public string WriterId { get; init; } = "analyst";

    public int PendingCount() => Pending().Count;

    public MergeResult Preview() => publishService.Preview(Pending());

    public PublishResult Publish(IReadOnlyList<ConflictResolution> resolutions)
    {
        lock (_gate)
        {
            IReadOnlyList<ChangeLogEntry> pending = Pending();
            PublishResult result = publishService.Publish(WriterId, pending, resolutions);
            if (result.Published && pending.Count > 0)
            {
                _lastPublishedSeq = pending.Max(e => e.ClientSeq);
            }

            return result;
        }
    }

    public RefreshResult Refresh()
    {
        FoldedState remote = ChangeFold.Fold(remoteStore.ReadAllChanges());
        FoldedState localKnown = BuildLocalState();
        HashSet<CellKey> pendingCells = Pending()
            .Select(e => new CellKey(e.Table, e.RowId, e.Column))
            .ToHashSet();

        RefreshPlan plan = planner.Plan(pendingCells, localKnown, remote);

        foreach (IGrouping<(string Table, Guid Row), CellChange> group in plan.Applied
            .GroupBy(c => (c.Cell.Table, c.Cell.RowId)))
        {
            Dictionary<string, string?> values = group.ToDictionary(c => c.Cell.Column, c => c.Value, StringComparer.Ordinal);
            localStore.AdoptCanonical(group.Key.Table, group.Key.Row, values);
        }

        return new RefreshResult(plan.Applied.Count, plan.Flagged.Count);
    }

    public IReadOnlyList<ChangeLogEntry> History(string? table = null, Guid? rowId = null) => audit.Query(table, rowId);

    private IReadOnlyList<ChangeLogEntry> Pending() => audit.Pending(_lastPublishedSeq);

    private FoldedState BuildLocalState()
    {
        FoldedState state = new();
        foreach (string table in catalog.GetTables())
        {
            FoldedTable folded = state.Table(table);
            foreach (Row row in localStore.GetAll(table, includeDeleted: true))
            {
                FoldedRow foldedRow = new(row.Id) { IsDeleted = row.IsDeleted };
                foreach ((string column, string? value) in row.Values)
                {
                    foldedRow.Values[column] = value;
                }

                folded.Rows[row.Id] = foldedRow;
            }
        }

        return state;
    }
}
