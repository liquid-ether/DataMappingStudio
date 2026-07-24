using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;

namespace App.Infrastructure.Remote.Tests;

/// <summary>A throwaway temp directory for remote-store tests.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dms-remote-" + Guid.NewGuid().ToString("N"));

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Deterministic clock.</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>Minimal in-memory catalog for tests that need snapshot columns.</summary>
public sealed class FakeCatalog(IReadOnlyList<ColumnCatalogEntry> entries) : ICatalog
{
    public IReadOnlyList<ColumnCatalogEntry> GetAll() => entries;

    public IReadOnlyList<ColumnCatalogEntry> GetForTable(string table) => entries.Where(e => e.TableName == table).ToList();

    public IReadOnlyList<string> GetTables() => entries.Select(e => e.TableName).Distinct().ToList();

    public void Seed(IEnumerable<ColumnCatalogEntry> newEntries) => throw new NotSupportedException();

    public void AddColumn(ColumnCatalogEntry entry) => throw new NotSupportedException();

    public void UpdateColumnMeta(ColumnCatalogEntry entry) => throw new NotSupportedException();
}

public static class RemoteTestData
{
    public static readonly DateTimeOffset T0 = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    public static ChangeLogEntry Entry(
        string table, Guid row, string column, string? oldValue, string? newValue,
        string by, long seq, int minute, ChangeOperation op = ChangeOperation.Update)
        => new()
        {
            ChangeId = Guid.NewGuid(),
            ChangeSetId = $"{by}-cs",
            Table = table,
            RowId = row,
            Column = column,
            OldValue = oldValue,
            NewValue = newValue,
            Operation = op,
            ChangedBy = by,
            ChangedAtUtc = T0.AddMinutes(minute),
            ClientSeq = seq,
        };
}
