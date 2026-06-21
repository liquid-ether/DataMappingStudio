using App.Domain.Data;

namespace App.Application.Abstractions;

/// <summary>
/// The shared remote store on the synced folder: per-writer append-only change logs plus materialized
/// snapshots (Architecture §6a). An analyst only ever writes their own log file, so publishing is a
/// pure append that cannot collide with another writer.
/// </summary>
public interface IRemoteStore
{
    /// <summary>Every writer's change-log entries (the fold input).</summary>
    IReadOnlyList<ChangeLogEntry> ReadAllChanges();

    /// <summary>One writer's change-log entries.</summary>
    IReadOnlyList<ChangeLogEntry> ReadWriterChanges(string writerId);

    /// <summary>Appends entries to the writer's own log (the only file they ever write).</summary>
    void AppendChanges(string writerId, IReadOnlyList<ChangeLogEntry> entries);

    /// <summary>Known writer ids (from the <c>_changes/</c> folder).</summary>
    IReadOnlyList<string> Writers();
}

/// <summary>Materializes table snapshots from the folded logs (read-optimization, Architecture §6a).</summary>
public interface ISnapshotBuilder
{
    /// <summary>
    /// (Re)builds <paramref name="table"/>'s snapshot from <paramref name="allChanges"/>, incrementally
    /// when a sidecar exists, writing atomically (temp + rename). <paramref name="columns"/> defines the
    /// snapshot's data columns and their types.
    /// </summary>
    void Rebuild(string table, IReadOnlyList<RemoteColumn> columns, IReadOnlyList<ChangeLogEntry> allChanges);

    /// <summary>Reads a materialized snapshot back (for reporting/verification); null if absent.</summary>
    RemoteTable? ReadSnapshot(string table);
}
