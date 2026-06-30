using System.Text;
using App.Application.Abstractions;
using App.Domain.Data;

namespace App.Infrastructure.Remote;

/// <summary>
/// Per-writer append-only change logs on the synced folder: <c>_changes/&lt;writer-id&gt;.&lt;ext&gt;</c>.
/// Only the owning writer ever writes their file, so appends never collide (Architecture §6a). Append
/// is implemented as read-concat-write via the format provider and finished with an atomic rename.
/// </summary>
public sealed class FileRemoteStore : IRemoteStore
{
    private readonly string _changesDir;
    private readonly IRemoteFormat _format;

    public FileRemoteStore(string rootFolder, IRemoteFormat format)
    {
        _format = format;
        _changesDir = Path.Combine(rootFolder, "_changes");
        Directory.CreateDirectory(_changesDir);
    }

    public IReadOnlyList<ChangeLogEntry> ReadAllChanges()
        => Writers().SelectMany(ReadWriterChanges).ToList();

    public IReadOnlyList<ChangeLogEntry> ReadWriterChanges(string writerId)
    {
        string path = LogPath(writerId);
        if (!File.Exists(path))
        {
            return [];
        }

        // Retry the read: a sync client may briefly hold the file open.
        RemoteTable table = RemoteIo.Retry(() => _format.Read(path));
        return table.Rows.Select(ChangeLogSchema.FromRow).ToList();
    }

    public void AppendChanges(string writerId, IReadOnlyList<ChangeLogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        List<ChangeLogEntry> all = [.. ReadWriterChanges(writerId), .. entries];
        RemoteTable table = new(ChangeLogSchema.Columns, all.Select(ChangeLogSchema.ToRow).ToList());
        RemoteIo.Retry(() => AtomicWrite.Write(LogPath(writerId), temp => _format.Write(temp, table)));
    }

    public IReadOnlyList<string> Writers()
        => RemoteIo.Retry(() => Directory.EnumerateFiles(_changesDir, $"*{_format.Extension}")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList());

    /// <summary>
    /// A cheap signature of the remote logs (file names + sizes + last-write times) — no content read.
    /// Lets the sync coordinator skip a full fold when nothing has changed remotely since the last refresh.
    /// </summary>
    public string RemoteVersion() => RemoteIo.Retry(() =>
    {
        if (!Directory.Exists(_changesDir))
        {
            return "0";
        }

        StringBuilder sb = new();
        foreach (string file in Directory.EnumerateFiles(_changesDir, $"*{_format.Extension}").OrderBy(f => f, StringComparer.Ordinal))
        {
            FileInfo info = new(file);
            sb.Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append('|');
        }

        return sb.ToString();
    });

    private string LogPath(string writerId) => Path.Combine(_changesDir, writerId + _format.Extension);
}
