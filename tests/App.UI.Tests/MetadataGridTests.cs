using AngleSharp.Dom;
using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;
using App.UI.Components;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

public class MetadataGridTests : AppTestContext
{
    private FakeCatalog _catalog = null!;

    // A single-column "name" table seeded with the given values, for sort/filter assertions.
    private IRenderedComponent<MetadataGrid> RenderNames(params string[] names)
    {
        _catalog = new FakeCatalog(
        [
            new ColumnCatalogEntry { TableName = "widget", ColumnName = "name", ValueType = CatalogValueType.Text, LabelEn = "Name", LabelFr = "Nom", DisplayOrder = 1 },
        ]);
        FakeLocalStore store = new();
        foreach (string n in names)
        {
            store.Upsert("widget", new Row("widget", Guid.NewGuid()) { ["name"] = n }, "s", "t");
        }

        Services.AddSingleton<ICatalog>(_catalog);
        Services.AddSingleton<ILocalStore>(store);
        Services.AddSingleton<LanguageState>();

        return Render<MetadataGrid>(p => p.Add(c => c.Table, "widget"));
    }

    private static List<string?> CellValues(IRenderedComponent<MetadataGrid> cut)
        => cut.FindAll("input.gi").Select(i => i.GetAttribute("value")).ToList();

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

    [Fact]
    public void Adding_an_invalid_column_name_shows_an_error_and_adds_nothing()
    {
        var cut = RenderGrid();

        ClickButton(cut, "Add column");
        cut.Find("input.ed-input").Change("my col"); // space -> not a valid identifier
        ClickButton(cut, "Save");

        Assert.Contains("use letters, digits and underscores", cut.Markup);
        Assert.DoesNotContain(_catalog.GetForTable("widget"), e => e.ColumnName == "my col");
    }

    [Fact]
    public void Adding_a_duplicate_column_name_shows_an_error()
    {
        var cut = RenderGrid();

        ClickButton(cut, "Add column");
        cut.Find("input.ed-input").Change("name"); // already exists
        ClickButton(cut, "Save");

        Assert.Contains("already exists", cut.Markup);
    }

    [Fact]
    public void A_store_failure_surfaces_a_friendly_message_instead_of_crashing()
    {
        _catalog = new FakeCatalog(
        [
            new ColumnCatalogEntry { TableName = "widget", ColumnName = "name", ValueType = CatalogValueType.Text, IsRequired = true, LabelEn = "Name", LabelFr = "Nom", DisplayOrder = 1 },
        ]);
        Services.AddSingleton<ICatalog>(_catalog);
        Services.AddSingleton<App.Application.Abstractions.ILocalStore>(new ThrowOnUpsertStore());
        Services.AddSingleton<LanguageState>();
        var cut = Render<MetadataGrid>(p => p.Add(c => c.Table, "widget"));

        ClickButton(cut, "Add row");
        cut.FindAll("input.gi")[0].Change("Widget A");
        ClickButton(cut, "Save"); // store throws -> caught, message shown, circuit survives

        Assert.Contains("could not be saved", cut.Markup);
    }

    // A store whose write fails, to exercise the grid's graceful error handling.
    private sealed class ThrowOnUpsertStore : App.Application.Abstractions.ILocalStore
    {
        private readonly FakeLocalStore _inner = new();
        public void EnsureSchema() => _inner.EnsureSchema();
        public Row? GetById(string table, Guid id) => _inner.GetById(table, id);
        public IReadOnlyList<Row> GetAll(string table, bool includeDeleted = false) => _inner.GetAll(table, includeDeleted);
        public IReadOnlyList<ChangeLogEntry> Upsert(string table, Row row, string changeSetId, string changedBy, ChangeOperation? operation = null)
            => throw new InvalidOperationException("simulated store failure");
        public IReadOnlyList<ChangeLogEntry> SoftDelete(string table, Guid id, string changeSetId, string changedBy) => _inner.SoftDelete(table, id, changeSetId, changedBy);
        public void AdoptCanonical(string table, Guid rowId, IReadOnlyDictionary<string, string?> values) => _inner.AdoptCanonical(table, rowId, values);
    }

    [Fact]
    public void Clicking_a_header_sorts_rows_ascending_then_descending()
    {
        var cut = RenderNames("Banana", "apple", "Cherry");

        cut.Find(".th-label").Click(); // ascending (case-insensitive)
        Assert.Equal(["apple", "Banana", "Cherry"], CellValues(cut));

        cut.Find(".th-label").Click(); // descending
        Assert.Equal(["Cherry", "Banana", "apple"], CellValues(cut));

        cut.Find(".th-label").Click(); // back to unsorted
        Assert.Equal(3, CellValues(cut).Count);
    }

    [Fact]
    public void Numeric_columns_sort_by_value_not_text()
    {
        _catalog = new FakeCatalog(
        [
            new ColumnCatalogEntry { TableName = "widget", ColumnName = "qty", ValueType = CatalogValueType.Integer, LabelEn = "Quantity", LabelFr = "Quantité", DisplayOrder = 1 },
        ]);
        FakeLocalStore store = new();
        foreach (string q in new[] { "2", "10", "1" })
        {
            store.Upsert("widget", new Row("widget", Guid.NewGuid()) { ["qty"] = q }, "s", "t");
        }

        Services.AddSingleton<ICatalog>(_catalog);
        Services.AddSingleton<ILocalStore>(store);
        Services.AddSingleton<LanguageState>();
        var cut = Render<MetadataGrid>(p => p.Add(c => c.Table, "widget"));

        cut.Find(".th-label").Click(); // ascending — numeric, so 1,2,10 (not 1,10,2)

        Assert.Equal(["1", "2", "10"], CellValues(cut));
    }

    [Fact]
    public void Filtering_a_column_hides_non_matching_rows()
    {
        var cut = RenderNames("apple", "apricot", "banana");

        ClickButton(cut, "Filters");              // reveal the per-column filter boxes
        cut.Find("input.th-filter").Input("ap");  // filter the name column

        List<string?> shown = CellValues(cut);
        Assert.Equal(2, shown.Count);
        Assert.Contains("apple", shown);
        Assert.Contains("apricot", shown);
        Assert.DoesNotContain("banana", shown);

        ClickButton(cut, "Clear filters");
        Assert.Equal(3, CellValues(cut).Count);
    }
}
