using System.Text.Json;
using App.Application.Abstractions;
using App.Application.Sync;
using App.Domain.Catalog;
using App.Domain.Data;

namespace App.Infrastructure.Remote;

/// <summary>
/// Materializes table snapshots from the folded per-writer logs (Architecture §6a). Snapshots are a
/// read-optimization, not the source of truth: rebuilt incrementally using a <c>_meta/&lt;table&gt;.json</c>
/// sidecar that records the per-writer ClientSeq already incorporated, and written atomically so a lost
/// rebuild race only ever lags briefly.
/// </summary>
public sealed class SnapshotBuilder : ISnapshotBuilder
{
    private const string IdColumn = SyncColumns.Id;
    private readonly string _root;
    private readonly string _metaDir;
    private readonly IRemoteFormat _format;
    // Snapshots are shared in the remote folder; serialize rebuilds so concurrent publishes (multiple
    // per-user workspaces on a host) can't interleave the read-fold-write of a table's snapshot/sidecar.
    private readonly object _gate = new();

    public SnapshotBuilder(string rootFolder, IRemoteFormat format)
    {
        _root = rootFolder;
        _format = format;
        _metaDir = Path.Combine(rootFolder, "_meta");
        Directory.CreateDirectory(_metaDir);
    }

    public void Rebuild(string table, IReadOnlyList<RemoteColumn> columns, IReadOnlyList<ChangeLogEntry> allChanges)
    {
        lock (_gate)
        {
            List<ChangeLogEntry> relevant = allChanges.Where(e => e.Table == table).ToList();
            Dictionary<string, long> sidecar = ReadSidecar(table);

            FoldedState state;
            if (sidecar.Count > 0 && File.Exists(SnapshotPath(table)))
            {
                // Incremental: seed from the existing snapshot, fold only entries newer than the sidecar.
                state = SeedFromSnapshot(table, columns);
                IEnumerable<ChangeLogEntry> newer = relevant.Where(e => e.ClientSeq > sidecar.GetValueOrDefault(e.ChangedBy, long.MinValue));
                ChangeFold.Apply(state, newer);
            }
            else
            {
                state = ChangeFold.Fold(relevant);
            }

            WriteSnapshot(table, columns, state);
            WriteSidecar(table, relevant);
        }
    }

    public RemoteTable? ReadSnapshot(string table)
        => File.Exists(SnapshotPath(table)) ? _format.Read(SnapshotPath(table)) : null;

    private void WriteSnapshot(string table, IReadOnlyList<RemoteColumn> columns, FoldedState state)
    {
        List<RemoteColumn> all = [new RemoteColumn(IdColumn, CatalogValueType.Uuid), .. columns];
        List<IReadOnlyList<string?>> rows = [];

        if (state.Tables.TryGetValue(table, out FoldedTable? folded))
        {
            foreach (FoldedRow row in folded.Rows.Values.Where(r => !r.IsDeleted).OrderBy(r => r.Id))
            {
                List<string?> cells = [row.Id.ToString()];
                cells.AddRange(columns.Select(c => row.Values.GetValueOrDefault(c.Name)));
                rows.Add(cells);
            }
        }

        RemoteTable snapshot = new(all, rows);
        AtomicWrite.Write(SnapshotPath(table), temp => _format.Write(temp, snapshot));
    }

    private FoldedState SeedFromSnapshot(string table, IReadOnlyList<RemoteColumn> columns)
    {
        FoldedState state = new();
        RemoteTable snapshot = _format.Read(SnapshotPath(table));
        int idIndex = IndexOf(snapshot, IdColumn);

        FoldedTable folded = state.Table(table);
        foreach (IReadOnlyList<string?> row in snapshot.Rows)
        {
            Guid id = Guid.Parse(row[idIndex]!);
            FoldedRow foldedRow = new(id);
            foreach (RemoteColumn column in columns)
            {
                int index = IndexOf(snapshot, column.Name);
                if (index >= 0 && index < row.Count)
                {
                    foldedRow.Values[column.Name] = row[index];
                }
            }

            folded.Rows[id] = foldedRow;
        }

        return state;
    }

    private void WriteSidecar(string table, IReadOnlyList<ChangeLogEntry> relevant)
    {
        Dictionary<string, long> perWriterMax = relevant
            .GroupBy(e => e.ChangedBy, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(e => e.ClientSeq), StringComparer.Ordinal);

        File.WriteAllText(SidecarPath(table), JsonSerializer.Serialize(perWriterMax));
    }

    private Dictionary<string, long> ReadSidecar(string table)
        => File.Exists(SidecarPath(table))
            ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(SidecarPath(table))) ?? []
            : [];

    private static int IndexOf(RemoteTable table, string column)
    {
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (table.Columns[i].Name == column)
            {
                return i;
            }
        }

        return -1;
    }

    private string SnapshotPath(string table) => Path.Combine(_root, table + _format.Extension);

    private string SidecarPath(string table) => Path.Combine(_metaDir, table + ".json");
}
