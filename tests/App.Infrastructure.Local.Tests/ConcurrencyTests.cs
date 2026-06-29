using App.Domain.Data;

namespace App.Infrastructure.Local.Tests;

/// <summary>
/// Verifies the store is safe under concurrent access (the web host shares one connection across many
/// Blazor circuits plus the auto-refresh timer). Without the <see cref="LocalDatabase.Gate"/> these
/// would intermittently throw ("database is locked" / reader conflicts) or corrupt the change log's
/// per-writer sequence; with it every operation is serialized and the data stays consistent.
/// </summary>
public class ConcurrencyTests
{
    [Fact]
    public async Task Parallel_writes_and_reads_do_not_corrupt_or_throw()
    {
        using LocalStoreFixture fx = new();
        const int writers = 8;
        const int perWriter = 60;

        // Many threads upsert distinct rows while others read concurrently.
        Task[] writes = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < perWriter; i++)
            {
                Guid id = Guid.NewGuid();
                Row row = new(LocalStoreFixture.Table, id) { ["name"] = $"w{w}-{i}", ["qty"] = i.ToString() };
                fx.Store.Upsert(LocalStoreFixture.Table, row, $"cs-{w}", $"writer{w}");
            }
        })).ToArray();

        Task[] reads = Enumerable.Range(0, 4).Select(reader => Task.Run(() =>
        {
            for (int i = 0; i < perWriter; i++)
            {
                _ = fx.Store.GetAll(LocalStoreFixture.Table);
                _ = fx.Audit.Query(LocalStoreFixture.Table);
            }
        })).ToArray();

        await Task.WhenAll(writes.Concat(reads));

        // Every row landed exactly once...
        Assert.Equal(writers * perWriter, fx.Store.GetAll(LocalStoreFixture.Table).Count);

        // ...and the append-only log assigned a unique, gap-checked sequence to every entry.
        IReadOnlyList<ChangeLogEntry> log = fx.Audit.Query(LocalStoreFixture.Table);
        Assert.Equal(log.Count, log.Select(e => e.ClientSeq).Distinct().Count());
    }
}
