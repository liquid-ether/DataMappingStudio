using AngleSharp.Dom;
using App.Application.Abstractions;
using App.Domain.Catalog;
using App.UI.Components;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

public class MetadataGridTests : AppTestContext
{
    private FakeCatalog _catalog = null!;

    private IRenderedComponent<MetadataGrid> RenderGrid()
    {
        _catalog = new FakeCatalog(
        [
            new ColumnCatalogEntry { TableName = "widget", ColumnName = "name", ValueType = CatalogValueType.Text, IsRequired = true, LabelEn = "Name", LabelFr = "Nom", DisplayOrder = 1 },
            new ColumnCatalogEntry { TableName = "widget", ColumnName = "qty", ValueType = CatalogValueType.Integer, LabelEn = "Quantity", LabelFr = "Quantité", DisplayOrder = 2 },
        ]);
        Services.AddSingleton<ICatalog>(_catalog);
        Services.AddSingleton<ILocalStore>(new FakeLocalStore());
        Services.AddSingleton<LanguageState>();

        return Render<MetadataGrid>(p => p.Add(c => c.Table, "widget"));
    }

    private static void ClickButton(IRenderedComponent<MetadataGrid> cut, string text)
        => cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    [Fact]
    public void Renders_headers_from_the_catalog_with_required_marker()
    {
        var cut = RenderGrid();

        Assert.Contains("Name", cut.Markup);
        Assert.Contains("Quantity", cut.Markup);
        Assert.Contains("req", cut.Markup); // required marker on name
    }

    [Fact]
    public void A_runtime_added_column_appears_via_the_add_column_flow()
    {
        var cut = RenderGrid();

        ClickButton(cut, "Add column");
        cut.Find("input.ed-input").Change("color");
        ClickButton(cut, "Save");

        Assert.Contains("color", cut.Markup);
        Assert.Contains(_catalog.GetForTable("widget"), e => e.ColumnName == "color");
    }

    [Fact]
    public void Add_row_persists_a_valid_row()
    {
        var cut = RenderGrid();

        ClickButton(cut, "Add row");
        cut.FindAll("input.gi")[0].Change("Widget A"); // name
        ClickButton(cut, "Save");

        Assert.Contains("Widget A", cut.Markup);
    }

    [Fact]
    public void Add_row_blocks_when_a_required_field_is_missing()
    {
        var cut = RenderGrid();

        ClickButton(cut, "Add row");
        ClickButton(cut, "Save"); // empty required name

        Assert.Contains("Fill all required fields.", cut.Markup);
    }
}
