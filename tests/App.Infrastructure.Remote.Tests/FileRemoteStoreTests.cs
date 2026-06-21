using App.Domain.Data;
using App.Infrastructure.Remote.Formats;
using static App.Infrastructure.Remote.Tests.RemoteTestData;

namespace App.Infrastructure.Remote.Tests;

public class FileRemoteStoreTests
{
    private static readonly Guid Row = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public void Two_writers_append_independently_and_read_all_combines()
    {
        using TempFolder dir = new();
        FileRemoteStore store = new(dir.Path, new CsvRemoteFormat());

        store.AppendChanges("alice", [Entry("t", Row, "name", null, "Alice", "alice", 1, 0)]);
        store.AppendChanges("bob", [Entry("t", Row, "city", null, "Lyon", "bob", 1, 1)]);

        Assert.Equal(["alice", "bob"], store.Writers());
        Assert.Single(store.ReadWriterChanges("alice"));
        Assert.Single(store.ReadWriterChanges("bob"));
        Assert.Equal(2, store.ReadAllChanges().Count);
    }

    [Fact]
    public void Appends_accumulate_in_the_writers_own_log()
    {
        using TempFolder dir = new();
        FileRemoteStore store = new(dir.Path, new ParquetRemoteFormat());

        store.AppendChanges("alice", [Entry("t", Row, "name", null, "Alice", "alice", 1, 0)]);
        store.AppendChanges("alice", [Entry("t", Row, "name", "Alice", "Bob", "alice", 2, 1)]);

        IReadOnlyList<ChangeLogEntry> all = store.ReadWriterChanges("alice");
        Assert.Equal(2, all.Count);
        Assert.Equal([1, 2], all.Select(e => e.ClientSeq).Order());
    }
}
