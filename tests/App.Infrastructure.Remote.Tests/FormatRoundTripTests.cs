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
}
