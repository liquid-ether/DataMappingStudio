using App.Application.Abstractions;
using App.Domain.Data;

namespace App.Infrastructure.Remote;

/// <summary>
/// Rolls change-log entries older than a threshold out of the live per-writer logs into dated archive
/// files (Architecture §6a compaction). The table snapshots already reflect the archived entries' net
/// effect; archives stay available for time-travel. Safe per writer (only that writer's file is touched).
/// </summary>
public sealed class LogCompactor(string rootFolder, IRemoteFormat format)
{
    public int Compact(int olderThanDays, DateTimeOffset now)
    {
        string changesDir = Path.Combine(rootFolder, "_changes");
        if (!Directory.Exists(changesDir))
        {
            return 0;
        }

        string archiveDir = Path.Combine(changesDir, "archive");
        Directory.CreateDirectory(archiveDir);
        DateTimeOffset cutoff = now.AddDays(-olderThanDays);
        int archived = 0;

        foreach (string file in Directory.EnumerateFiles(changesDir, $"*{format.Extension}"))
        {
            List<ChangeLogEntry> entries = format.Read(file).Rows.Select(ChangeLogSchema.FromRow).ToList();
            List<ChangeLogEntry> old = entries.Where(e => e.ChangedAtUtc < cutoff).ToList();
            if (old.Count == 0)
            {
                continue;
            }

            List<ChangeLogEntry> recent = entries.Where(e => e.ChangedAtUtc >= cutoff).ToList();
            string writer = Path.GetFileNameWithoutExtension(file);
            string archivePath = Path.Combine(archiveDir, writer + format.Extension);

            List<ChangeLogEntry> archive = File.Exists(archivePath)
                ? [.. format.Read(archivePath).Rows.Select(ChangeLogSchema.FromRow), .. old]
                : old;

            Write(archivePath, archive);
            Write(file, recent);
            archived += old.Count;
        }

        return archived;
    }

    private void Write(string path, IReadOnlyList<ChangeLogEntry> entries)
        => AtomicWrite.Write(path, temp => format.Write(temp, new RemoteTable(ChangeLogSchema.Columns, entries.Select(ChangeLogSchema.ToRow).ToList())));
}
