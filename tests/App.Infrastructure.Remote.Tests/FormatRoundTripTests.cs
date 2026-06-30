using System.Collections.Concurrent;
using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Infrastructure.Remote.Formats;

namespace App.Infrastructure.Remote.Tests;

public class FormatRoundTripTests
{
    public static TheoryData<IRemoteFormat> Formats() =>
    [
        new ParquetRemoteFormat(),
        new CsvRemoteFormat(),
        new ExcelRemoteFormat(),
    ];

    private static RemoteTable Sample() => new(
        [
            new RemoteColumn("name", CatalogValueType.Text),
            new RemoteColumn("qty", CatalogValueType.Integer),
            new RemoteColumn("price", CatalogValueType.Number),
            new RemoteColumn("active", CatalogValueType.Boolean),
        ],
        [
            ["Widget A", "5", "9.99", "true"],
            ["Wid,get \"B\"", "0", "1234.5", "false"],
            ["Nullish", null, null, null],
        ]);

    [Theory]
    [MemberData(nameof(Formats))]
    public void Values_and_names_round_trip(IRemoteFormat format)
    {
        using TempFolder dir = new();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "data" + format.Extension);
        RemoteTable original = Sample();

        format.Write(path, original);
        RemoteTable read = format.Read(path);

        Assert.Equal(original.Columns.Select(c => c.Name), read.Columns.Select(c => c.Name));
        Assert.Equal(original.Rows.Count, read.Rows.Count);
        for (int r = 0; r < original.Rows.Count; r++)
        {
            for (int c = 0; c < original.Columns.Count; c++)
            {
                Assert.Equal(original.Rows[r][c], read.Rows[r][c]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Empty_table_round_trips(IRemoteFormat format)
    {
        using TempFolder dir = new();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "empty" + format.Extension);
        RemoteTable original = RemoteTable.Empty([new RemoteColumn("name", CatalogValueType.Text)]);

        format.Write(path, original);
        RemoteTable read = format.Read(path);

        Assert.Empty(read.Rows);
    }

    // Regression: the Parquet format bridges Parquet.Net's async API synchronously. Run from a
    // single-threaded SynchronizationContext (as the Blazor Server circuit does), a naive sync-over-async
    // bridge deadlocks because the library's continuations post back to the one blocked thread. The format
    // must escape the captured context (Task.Run) so a synchronous publish from the web host completes.
    [Fact]
    public void Parquet_write_and_read_do_not_deadlock_under_a_single_threaded_context()
    {
        using TempFolder dir = new();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "ctx.parquet");
        ParquetRemoteFormat format = new();
        RemoteTable original = Sample();

        Exception? error = null;
        bool completed = false;
        using ManualResetEventSlim done = new();

        Thread thread = new(() =>
        {
            SingleThreadedSyncContext ctx = new();
            SynchronizationContext.SetSynchronizationContext(ctx);
            ctx.Post(_ =>
            {
                try
                {
                    format.Write(path, original);
                    RemoteTable read = format.Read(path);
                    completed = read.Rows.Count == original.Rows.Count;
                }
                catch (Exception ex) { error = ex; }
                finally { ctx.Complete(); }
            }, null);
            ctx.RunOnCurrentThread();
            done.Set();
        }) { IsBackground = true };

        thread.Start();

        Assert.True(done.Wait(TimeSpan.FromSeconds(15)), "Parquet IO deadlocked under a single-threaded SynchronizationContext.");
        Assert.Null(error);
        Assert.True(completed);
    }

    /// <summary>A minimal single-threaded message pump, mimicking the Blazor Server circuit's context.</summary>
    private sealed class SingleThreadedSyncContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void Complete() => _queue.CompleteAdding();

        public void RunOnCurrentThread()
        {
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
