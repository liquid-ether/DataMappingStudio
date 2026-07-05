using System.Diagnostics;
using App.Application.Abstractions;
using App.Application.Diagnostics;
using App.Domain.Data;

namespace App.Application.Sync;

public sealed record RefreshResult(int Applied, int Flagged);

/// <summary>
/// Health of the shared remote folder, surfaced to the UI so an offline / locked OneDrive share is
/// visible instead of silently failing in the background.
/// </summary>
public sealed record RemoteHealth(bool Available, string? Message, DateTimeOffset? LastSuccessUtc)
{
    public static readonly RemoteHealth Unknown = new(true, null, null);
    public static RemoteHealth Ok(DateTimeOffset now, DateTimeOffset? _ = null) => new(true, null, now);
    public static RemoteHealth Down(string message, DateTimeOffset? lastSuccess) => new(false, message, lastSuccess);
}

/// <summary>
/// Coordinates publish / preview / auto-refresh / history for the UI, tracking the analyst's
/// last-published sequence so "pending" means local change-log entries newer than that.
/// </summary>
public interface ISyncCoordinator
{
    string WriterId { get; }

    /// <summary>Last-known health of the remote folder (updated on every refresh / publish).</summary>
    RemoteHealth RemoteStatus { get; }

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
    AutoRefreshPlanner planner,
    RemoteFoldCache? foldCache = null,
    CatalogSyncService? catalogSync = null,
    ITableCatalog? tableCatalog = null) : ISyncCoordinator
{
    private readonly object _gate = new();
    private long? _lastPublishedSeq;
    private string? _lastRemoteVersion;
    private string? _lastCatalogVersion;

    // Loaded lazily from the working copy (and persisted on publish) so "pending" survives restarts.
    private long LastPublishedSeq => _lastPublishedSeq ??= audit.GetPublishCheckpoint();
    private RemoteHealth _remoteStatus = RemoteHealth.Unknown;

    public string WriterId { get; init; } = "analyst";

    public RemoteHealth RemoteStatus => _remoteStatus;

    public int PendingCount() => Pending().Count;

    public MergeResult Preview() => publishService.Preview(Pending());

    public PublishResult Publish(IReadOnlyList<ConflictResolution> resolutions)
    {
        lock (_gate)
        {
            long started = Stopwatch.GetTimestamp();
            IReadOnlyList<ChangeLogEntry> pending = Pending();
            try
            {
                PublishResult result = publishService.Publish(WriterId, pending, resolutions);
                AppMetrics.PublishDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                AppMetrics.PublishedChanges.Add(result.AppendedCount);
                AppMetrics.PublishConflicts.Add(result.Conflicts.Count);
                if (result.Published && pending.Count > 0)
                {
                    long checkpoint = pending.Max(e => e.ClientSeq);
                    _lastPublishedSeq = checkpoint;
                    audit.SetPublishCheckpoint(checkpoint); // survive restarts / workspace re-creation
                }

                _lastRemoteVersion = null; // our own write changed the remote; force a refold next refresh
                MarkRemoteOk();
                return result;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MarkRemoteDown(ex);
                throw;
            }
        }
    }

    public RefreshResult Refresh()
    {
        long started = Stopwatch.GetTimestamp();
        FoldedState remote;
        try
        {
            // Meta-model first (version-gated): a table/column another writer added must exist locally
            // BEFORE its data cells fold in, or they'd be skipped as unknown.
            if (catalogSync is not null && tableCatalog is not null)
            {
                string catalogVersion = catalogSync.Version();
                if (catalogVersion != _lastCatalogVersion)
                {
                    catalogSync.ApplyTo(catalog, tableCatalog, localStore);
                    _lastCatalogVersion = catalogVersion;
                }
            }

            // Skip the (full) fold when nothing changed remotely since the last successful refresh.
            string version = remoteStore.RemoteVersion();
            if (version == _lastRemoteVersion)
            {
                MarkRemoteOk();
                return new RefreshResult(0, 0);
            }

            // On a multi-user host the (optional) shared cache means one fold per remote change, not one
            // per workspace. Slight staleness is fine: a version bump next cycle triggers a refold.
            remote = foldCache?.GetOrFold(remoteStore, version) ?? ChangeFold.Fold(remoteStore.ReadAllChanges());
            _lastRemoteVersion = version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MarkRemoteDown(ex);
            return new RefreshResult(0, 0);
        }

        FoldedState localKnown = BuildLocalState();
        HashSet<CellKey> pendingCells = Pending()
            .Select(e => new CellKey(e.Table, e.RowId, e.Column))
            .ToHashSet();

        RefreshPlan plan = planner.Plan(pendingCells, localKnown, remote);

        // Only adopt tables AND columns this catalog actually has: the shared folder may carry logs for
        // tables/columns this copy doesn't know yet (another writer's newer meta-model, or stale data),
        // and we must skip them rather than crash mid-adopt. Skipped cells are re-offered on the next
        // refresh after the catalog catches up. The applied count reflects only what was adopted.
        HashSet<string> known = catalog.GetTables().ToHashSet(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> knownColumns = known.ToDictionary(
            t => t,
            t => catalog.GetForTable(t).Select(c => c.ColumnName).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        int applied = 0;
        foreach (IGrouping<(string Table, Guid Row), CellChange> group in plan.Applied
            .Where(c => known.Contains(c.Cell.Table) && knownColumns[c.Cell.Table].Contains(c.Cell.Column))
            .GroupBy(c => (c.Cell.Table, c.Cell.RowId)))
        {
            Dictionary<string, string?> values = group.ToDictionary(c => c.Cell.Column, c => c.Value, StringComparer.Ordinal);
            localStore.AdoptCanonical(group.Key.Table, group.Key.Row, values);
            applied += values.Count;
        }

        MarkRemoteOk();
        AppMetrics.RefreshDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        AppMetrics.RefreshApplied.Add(applied);
        return new RefreshResult(applied, plan.Flagged.Count);
    }

    private void MarkRemoteOk() => _remoteStatus = RemoteHealth.Ok(DateTimeOffset.UtcNow);

    private void MarkRemoteDown(Exception ex) => _remoteStatus = RemoteHealth.Down(ex.Message, _remoteStatus.LastSuccessUtc);

    public IReadOnlyList<ChangeLogEntry> History(string? table = null, Guid? rowId = null) => audit.Query(table, rowId);

    private IReadOnlyList<ChangeLogEntry> Pending() => audit.Pending(LastPublishedSeq);

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
