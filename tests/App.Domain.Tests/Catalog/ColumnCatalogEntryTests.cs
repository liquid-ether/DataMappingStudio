using App.Domain.Catalog;

namespace App.Domain.Tests.Catalog;

public class ColumnCatalogEntryTests
{
    private static ColumnCatalogEntry Scalar() => new()
    {
        TableName = "data_source",
        ColumnName = "name",
        Kind = ColumnKind.Scalar,
        ValueType = CatalogValueType.Text,
        MaxLength = 100,
    };

    [Fact]
    public void Valid_scalar_entry_has_no_errors()
    {
        Assert.True(Scalar().IsValid);
    }

    [Fact]
    public void Reference_entry_requires_a_target()
    {
        ColumnCatalogEntry entry = Scalar() with { Kind = ColumnKind.Reference, ReferenceTarget = null };

        Assert.Contains(entry.Validate(), e => e.Contains("ReferenceTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void Computed_entry_requires_a_formula()
    {
        ColumnCatalogEntry entry = Scalar() with { Kind = ColumnKind.Computed, Formula = null };

        Assert.Contains(entry.Validate(), e => e.Contains("Formula", StringComparison.Ordinal));
    }

    [Fact]
    public void Scalar_entry_may_not_carry_a_reference_target()
    {
        ColumnCatalogEntry entry = Scalar() with { ReferenceTarget = "lookup_value" };

        Assert.Contains(entry.Validate(), e => e.Contains("Only reference columns", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_names_are_invalid(string blank)
    {
        ColumnCatalogEntry entry = Scalar() with { ColumnName = blank };

        Assert.Contains(entry.Validate(), e => e.Contains("ColumnName", StringComparison.Ordinal));
    }
}
