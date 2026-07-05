using App.Application.Abstractions;
using App.Application.References;
using App.Domain.Catalog;

namespace App.Application.Tests.References;

public class ComputedFormulaTests
{
    [Theory]
    [InlineData("count(dictionary_entry.source_id)")]
    [InlineData("  COUNT( dictionary_entry.source_id )  ")]
    public void Parses_count(string formula)
    {
        var parsed = Assert.IsType<ComputedFormula.Count>(ComputedFormula.Parse(formula));
        Assert.Equal("dictionary_entry", parsed.ChildTable);
        Assert.Equal("source_id", parsed.FkColumn);
    }

    [Fact]
    public void Parses_reference_lookup()
    {
        var parsed = Assert.IsType<ComputedFormula.RefLookup>(ComputedFormula.Parse("lookup(classification_id.access_901)"));
        Assert.Equal("classification_id", parsed.RefColumn);
        Assert.Equal("access_901", parsed.TargetColumn);
    }

    [Theory]
    [InlineData("lookup(app_code, application, description)", null)]
    [InlineData("lookup(app_code, application.app_code, description)", "app_code")]
    public void Parses_key_lookup(string formula, string? match)
    {
        var parsed = Assert.IsType<ComputedFormula.KeyLookup>(ComputedFormula.Parse(formula));
        Assert.Equal("app_code", parsed.KeyColumn);
        Assert.Equal("application", parsed.TargetTable);
        Assert.Equal(match, parsed.MatchColumn);
        Assert.Equal("description", parsed.ReturnColumn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sum(a.b)")]
    [InlineData("lookup()")]
    [InlineData("lookup(a, b)")]
    [InlineData("CONCAT(a, b, c, d)")]
    public void Rejects_everything_else(string formula) => Assert.Null(ComputedFormula.Parse(formula));

    // ---- Validate ----

    private sealed class StubCatalog : ICatalog
    {
        private readonly List<ColumnCatalogEntry> _entries =
        [
            new() { TableName = "vendor", ColumnName = "code", ValueType = CatalogValueType.Text },
            new() { TableName = "vendor", ColumnName = "app_id", Kind = ColumnKind.Reference, ValueType = CatalogValueType.Uuid, ReferenceTarget = "application" },
            new() { TableName = "application", ColumnName = "app_code", ValueType = CatalogValueType.Text },
            new() { TableName = "application", ColumnName = "description", ValueType = CatalogValueType.Text },
        ];

        public IReadOnlyList<ColumnCatalogEntry> GetAll() => _entries;
        public IReadOnlyList<ColumnCatalogEntry> GetForTable(string table) => _entries.Where(e => e.TableName == table).ToList();
        public IReadOnlyList<string> GetTables() => _entries.Select(e => e.TableName).Distinct().ToList();
        public void Seed(IEnumerable<ColumnCatalogEntry> entries) => throw new NotSupportedException();
        public void AddColumn(ColumnCatalogEntry entry) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("count(vendor.app_id)")]                                // valid count
    [InlineData("lookup(app_id.description)")]                          // valid ref lookup
    [InlineData("lookup(code, application.app_code, description)")]     // valid key lookup w/ match
    public void Validate_accepts_well_formed_formulas(string formula)
        => Assert.Empty(ComputedFormula.Validate("vendor", formula, new StubCatalog()));

    [Theory]
    [InlineData("count(nope.fk)", "Unknown table")]
    [InlineData("lookup(code.description)", "not a reference")]
    [InlineData("lookup(code, application.app_code, nope)", "no column 'nope'")]
    [InlineData("gibberish", "Unrecognized formula")]
    public void Validate_reports_the_problem(string formula, string expectedFragment)
    {
        IReadOnlyList<string> errors = ComputedFormula.Validate("vendor", formula, new StubCatalog());
        Assert.Contains(errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Key_lookup_without_match_column_requires_a_display_column()
    {
        // "application" has a built-in display column, so the 3-arg form is fine there…
        Assert.Empty(ComputedFormula.Validate("vendor", "lookup(code, application, description)", new StubCatalog()));

        // …but a table with no display metadata demands an explicit match column.
        IReadOnlyList<string> errors = ComputedFormula.Validate("application", "lookup(app_code, vendor, code)", new StubCatalog());
        Assert.Contains(errors, e => e.Contains("display column", StringComparison.OrdinalIgnoreCase));
    }
}
