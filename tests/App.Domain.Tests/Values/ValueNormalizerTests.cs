using App.Domain.Catalog;
using App.Domain.Values;

namespace App.Domain.Tests.Values;

public class ValueNormalizerTests
{
    [Theory]
    [InlineData(CatalogValueType.Text, "  hello  ", "hello")]
    [InlineData(CatalogValueType.Integer, " 0042 ", "42")]
    [InlineData(CatalogValueType.Number, "5.00", "5")]
    [InlineData(CatalogValueType.Number, "5.250", "5.25")]
    [InlineData(CatalogValueType.Boolean, "Vrai", "true")]
    [InlineData(CatalogValueType.Boolean, "0", "false")]
    [InlineData(CatalogValueType.Boolean, "YES", "true")]
    [InlineData(CatalogValueType.Uuid, "  D9B1F8C0-0000-0000-0000-000000000000  ", "d9b1f8c0-0000-0000-0000-000000000000")]
    [InlineData(CatalogValueType.Date, "2026-06-21", "2026-06-21")]
    public void Normalize_produces_canonical_form(CatalogValueType type, string raw, string expected)
    {
        Assert.Equal(expected, ValueNormalizer.Normalize(type, raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_normalizes_to_null(string? raw)
    {
        Assert.Null(ValueNormalizer.Normalize(CatalogValueType.Text, raw));
    }

    [Fact]
    public void Equivalent_number_formats_compare_equal_after_normalization()
    {
        string? a = ValueNormalizer.Normalize(CatalogValueType.Number, "1200");
        string? b = ValueNormalizer.Normalize(CatalogValueType.Number, "1200.00");

        Assert.Equal(a, b);
    }

    [Fact]
    public void French_decimal_comma_is_accepted()
    {
        Assert.Equal("1234.5", ValueNormalizer.Normalize(CatalogValueType.Number, "1234,5"));
    }

    [Theory]
    [InlineData(CatalogValueType.Integer, "abc")]
    [InlineData(CatalogValueType.Boolean, "maybe")]
    [InlineData(CatalogValueType.Uuid, "not-a-guid")]
    public void Malformed_input_throws(CatalogValueType type, string raw)
    {
        Assert.Throws<FormatException>(() => ValueNormalizer.Normalize(type, raw));
    }
}
