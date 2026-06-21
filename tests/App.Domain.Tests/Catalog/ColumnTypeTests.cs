using App.Domain.Catalog;

namespace App.Domain.Tests.Catalog;

public class ColumnTypeTests
{
    [Theory]
    [InlineData("UUID", CatalogValueType.Uuid, null)]
    [InlineData("UniqueID", CatalogValueType.Uuid, null)]
    [InlineData("INT", CatalogValueType.Integer, null)]
    [InlineData("Boolean", CatalogValueType.Boolean, null)]
    [InlineData("TIMESTAMP", CatalogValueType.Timestamp, null)]
    [InlineData("DATE", CatalogValueType.Date, null)]
    [InlineData("DECIMAL", CatalogValueType.Number, null)]
    [InlineData("VARCHAR(50)", CatalogValueType.Text, 50)]
    [InlineData("VARCHAR(255)", CatalogValueType.Text, 255)]
    [InlineData("varchar(max)", CatalogValueType.Text, null)]
    [InlineData("  VARCHAR(MAX)  ", CatalogValueType.Text, null)]
    public void Parse_maps_workbook_annotations(string annotation, CatalogValueType expectedType, int? expectedMax)
    {
        ColumnType type = ColumnType.Parse(annotation);

        Assert.Equal(expectedType, type.ValueType);
        Assert.Equal(expectedMax, type.MaxLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("WIDGET")]
    [InlineData("VARCHAR()")]
    public void Parse_rejects_invalid_annotations(string annotation)
    {
        Assert.Throws<FormatException>(() => ColumnType.Parse(annotation));
    }

    [Fact]
    public void TryParse_reports_failure_without_throwing()
    {
        bool ok = ColumnType.TryParse("nope", out _, out string? error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
