using App.Application.Abstractions;
using App.Domain.Catalog;
using App.UI.Admin;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MetaModelService = App.Application.Catalog.MetaModelService;

namespace App.UI.Tests;

/// <summary>
/// The meta-model admin's label handling: bilingual labels are captured when adding a column (with
/// name-fallback when blank), and can be edited afterwards for both the table and its columns — the
/// path that feeds every grid header and the wizard's mapping dropdown.
/// </summary>
public class ModelAdminTests : AppTestContext
{
    private FakeCatalog _catalog = null!;
    private FakeTableCatalog _tables = null!;

    private IRenderedComponent<ModelAdmin> RenderAdmin()
    {
        _catalog = new FakeCatalog(
        [
            new ColumnCatalogEntry { TableName = "widget", ColumnName = "name", ValueType = CatalogValueType.Text, IsRequired = true, LabelEn = "Name", LabelFr = "Nom", DisplayOrder = 1 },
        ]);
        _tables = new FakeTableCatalog([new TableCatalogEntry { TableName = "widget", LabelEn = "Widgets", LabelFr = "Widgets", NavVisible = true }]);
        FakeLocalStore store = new();

        Services.AddSingleton<ICatalog>(_catalog);
        Services.AddSingleton<ITableCatalog>(_tables);
        Services.AddSingleton<ILocalStore>(store);
        Services.AddSingleton<LanguageState>();
        Services.AddSingleton(new MetaModelService(_catalog, _tables, store, new EnvironmentCurrentUser()));

        return Render<ModelAdmin>();
    }

    private static void ClickButton(IRenderedComponent<ModelAdmin> cut, string text)
        => cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    [Fact]
    public void Adding_a_column_captures_bilingual_labels()
    {
        var cut = RenderAdmin();

        ClickButton(cut, "Add column");
        cut.Find("input[placeholder=column_name]").Change("color");
        cut.Find("input[placeholder='Label (EN)']").Change("Color");
        cut.Find("input[placeholder='Label (FR)']").Change("Couleur");
        ClickButton(cut, "Save");

        ColumnCatalogEntry added = _catalog.GetForTable("widget").Single(c => c.ColumnName == "color");
        Assert.Equal("Color", added.LabelEn);
        Assert.Equal("Couleur", added.LabelFr);
        Assert.True(added.IsUserAdded);
    }

    [Fact]
    public void Blank_labels_fall_back_to_the_column_name_then_english()
    {
        var cut = RenderAdmin();

        ClickButton(cut, "Add column");
        cut.Find("input[placeholder=column_name]").Change("tier");
        cut.Find("input[placeholder='Label (EN)']").Change("Tier"); // FR left blank -> EN
        ClickButton(cut, "Save");

        ColumnCatalogEntry added = _catalog.GetForTable("widget").Single(c => c.ColumnName == "tier");
        Assert.Equal("Tier", added.LabelEn);
        Assert.Equal("Tier", added.LabelFr);
    }

    [Fact]
    public void Editing_labels_updates_the_table_and_its_columns()
    {
        var cut = RenderAdmin();

        ClickButton(cut, "Edit labels");
        cut.FindAll("input[placeholder='Label (EN)']")[0].Change("Gadgets");        // table label
        cut.FindAll("input[placeholder='Label (FR)']")[0].Change("Gadgets FR");
        cut.FindAll("input[placeholder='Label (EN)']")[1].Change("Widget name");    // 'name' column label
        cut.FindAll("input[placeholder='Label (FR)']")[1].Change("Nom du widget");
        ClickButton(cut, "Save");

        Assert.Equal("Gadgets", _tables.GetTableMeta("widget")!.LabelEn);
        ColumnCatalogEntry name = _catalog.GetForTable("widget").Single(c => c.ColumnName == "name");
        Assert.Equal("Widget name", name.LabelEn);
        Assert.Equal("Nom du widget", name.LabelFr);
        Assert.Contains("updated", cut.Markup); // confirmation message
    }

    [Fact]
    public void Editing_a_table_changes_its_display_column()
    {
        var cut = RenderAdmin();

        ClickButton(cut, "Edit labels");
        cut.Find("select.th-filter").Change("name"); // the only display-column select in the row
        ClickButton(cut, "Save");

        Assert.Equal("name", _tables.GetTableMeta("widget")!.DisplayColumn);
    }

    [Fact]
    public void Creating_a_table_carries_column_labels()
    {
        var cut = RenderAdmin();

        ClickButton(cut, "New table");
        cut.Find("input[placeholder=table_name]").Change("vendor");
        cut.FindAll("input[placeholder='Label (EN)']")[0].Change("Vendors");        // table labels
        cut.FindAll("input[placeholder='Label (FR)']")[0].Change("Fournisseurs");
        cut.Find("input[placeholder=column_name]").Change("code");
        cut.FindAll("input[placeholder='Label (EN)']")[1].Change("Vendor code");    // column labels
        cut.FindAll("input[placeholder='Label (FR)']")[1].Change("Code fournisseur");
        ClickButton(cut, "Create table");

        Assert.Equal("Vendors", _tables.GetTableMeta("vendor")!.LabelEn);
        ColumnCatalogEntry code = _catalog.GetForTable("vendor").Single(c => c.ColumnName == "code");
        Assert.Equal("Vendor code", code.LabelEn);
        Assert.Equal("Code fournisseur", code.LabelFr);
    }
}
