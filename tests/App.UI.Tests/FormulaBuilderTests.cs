using App.Application.Abstractions;
using App.Application.Provisioning;
using App.Domain.Catalog;
using App.Domain.Entities;
using App.UI.Admin;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

public class FormulaBuilderTests : AppTestContext
{
    public FormulaBuilderTests()
    {
        Services.AddSingleton<LanguageState>();
        Services.AddSingleton<ICatalog>(new FakeCatalog(DefaultCatalog.Entries()));
    }

    private static readonly IReadOnlyList<ColumnCatalogEntry> LocalColumns =
    [
        new() { TableName = "vendor", ColumnName = "code", ValueType = CatalogValueType.Text },
        new() { TableName = "vendor", ColumnName = "app_id", Kind = ColumnKind.Reference, ValueType = CatalogValueType.Uuid, ReferenceTarget = TableNames.Application },
    ];

    [Fact]
    public void Count_kind_emits_a_count_formula_from_cascading_dropdowns()
    {
        string? formula = null;
        var cut = Render<FormulaBuilder>(p => p
            .Add(x => x.LocalColumns, LocalColumns)
            .Add(x => x.ValueChanged, (string? v) => formula = v));

        cut.FindAll("select")[0].Change("count");
        cut.FindAll("select")[1].Change(TableNames.DictionaryEntry);   // child table
        cut.FindAll("select")[2].Change("source_id");                  // FK column

        Assert.Equal("count(dictionary_entry.source_id)", formula);
        Assert.Contains("count(dictionary_entry.source_id)", cut.Markup);
    }

    [Fact]
    public void Key_lookup_kind_emits_the_three_argument_lookup()
    {
        string? formula = null;
        var cut = Render<FormulaBuilder>(p => p
            .Add(x => x.LocalColumns, LocalColumns)
            .Add(x => x.ValueChanged, (string? v) => formula = v));

        cut.FindAll("select")[0].Change("key");
        cut.FindAll("select")[1].Change("code");                       // key column (local)
        cut.FindAll("select")[2].Change(TableNames.Application);       // target table
        cut.FindAll("select")[4].Change("description");                // return column (match left default)

        Assert.Equal("lookup(code, application, description)", formula);
    }

    [Fact]
    public void Reference_lookup_offers_only_reference_columns()
    {
        var cut = Render<FormulaBuilder>(p => p.Add(x => x.LocalColumns, LocalColumns));

        cut.FindAll("select")[0].Change("ref");

        var options = cut.FindAll("select")[1].QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Contains("app_id", options);
        Assert.DoesNotContain("code", options); // scalar — not offered as a reference
    }
}
