using App.Domain.Data;

namespace App.Application.Abstractions;

/// <summary>
/// The local append-only change log, which <em>is</em> the audit trail (Architecture §8). Every field
/// edit is recorded here as it happens; on publish these entries are appended verbatim to the
/// analyst's own remote log.
/// </summary>
public interface IAuditLog
{
    /// <summary>Appends field-change entries, assigning each a monotonic per-writer ClientSeq.</summary>
    /// <returns>The appended entries with their assigned <see cref="ChangeLogEntry.ClientSeq"/>.</returns>
    IReadOnlyList<ChangeLogEntry> Append(IEnumerable<ChangeLogEntry> entries);

    /// <summary>Queries the change log, optionally filtered by table and/or row, oldest first.</summary>
    IReadOnlyList<ChangeLogEntry> Query(string? table = null, Guid? rowId = null);

    /// <summary>Entries not yet published (after the given ClientSeq), oldest first.</summary>
    IReadOnlyList<ChangeLogEntry> Pending(long afterClientSeq);

    /// <summary>
    /// The highest ClientSeq already published to the remote (0 when never published). Persisted with the
    /// working copy so "pending" survives restarts — otherwise every historical edit would look pending
    /// again after a relaunch/workspace re-creation, inflating the badge and resurrecting long-published
    /// edits as spurious conflicts.
    /// </summary>
    long GetPublishCheckpoint();

    /// <summary>Records the highest ClientSeq included in a successful publish.</summary>
    void SetPublishCheckpoint(long clientSeq);
}
