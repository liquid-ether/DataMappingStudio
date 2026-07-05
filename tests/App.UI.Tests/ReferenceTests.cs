using App.Application.Abstractions;
using App.Application.Provisioning;
using App.Application.References;
using App.Domain.Data;
using App.Domain.Entities;
using App.UI.Components;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

public class ReferenceTests : AppTestContext
{
    private static readonly Guid AppId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid ClassId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

    private static (FakeCatalog Catalog, FakeLocalStore Store) Seeded()
    {
        FakeCatalog catalog = new(DefaultCatalog.Entries());
        FakeLocalStore store = new();
        store.Upsert(TableNames.Application, new Row(TableNames.Application, AppId) { ["app_code"] = "GDM1" }, "s", "t");
        store.Upsert(TableNames.Classification, new Row(TableNames.Classification, ClassId) { ["prp"] = "P1", ["access_901"] = "Open" }, "s", "t");
        store.Upsert(TableNames.DataSource, new Row(TableNames.DataSource, SourceId) { ["name"] = "Orders", ["application_id"] = AppId.ToString() }, "s", "t");
        store.Upsert(TableNames.DictionaryEntry, new Row(TableNames.DictionaryEntry, Guid.NewGuid()) { ["column_name"] = "c1", ["source_id"] = SourceId.ToString() }, "s", "t");
        store.Upsert(TableNames.DictionaryEntry, new Row(TableNames.DictionaryEntry, Guid.NewGuid()) { ["column_name"] = "c2", ["source_id"] = SourceId.ToString() }, "s", "t");
        return (catalog, store);
    }

    [Fact]
    public void Reference_options_and_display_resolve_against_the_store()
    {
        (FakeCatalog catalog, FakeLocalStore store) = Seeded();
        ReferenceService svc = new(catalog, store);

        Assert.Contains(svc.Options(TableNames.Application), o => o.Id == AppId && o.Display == "GDM1");
        Assert.Equal("GDM1", svc.Display(TableNames.Application, AppId.ToString()));
    }

    [Fact]
    public void Computed_count_evaluates_child_rows()
    {
        (FakeCatalog catalog, FakeLocalStore store) = Seeded();
        ReferenceService svc = new(catalog, store);
        Row source = store.GetById(TableNames.DataSource, SourceId)!;

        Assert.Equal("2", svc.Evaluate(TableNames.DataSource, "field_count", source));
    }

    [Fact]
    public void Computed_lookup_autofills_from_the_referenced_row()
    {
        (FakeCatalog catalog, FakeLocalStore store) = Seeded();
        ReferenceService svc = new(catalog, store);
        Row entry = new(TableNames.DictionaryEntry, Guid.NewGuid()) { ["classification_id"] = ClassId.ToString() };

        Assert.Equal("Open", svc.Evaluate(TableNames.DictionaryEntry, "access_901", entry));
    }

    [Fact]
    public void Key_lookup_matches_by_value_in_single_and_bulk_evaluation()
    {
        (FakeCatalog catalog, FakeLocalStore store) = Seeded();
        store.Upsert(TableNames.Application, new Row(TableNames.Application, AppId) { ["app_code"] = "GDM1", ["description"] = "Core banking" }, "s", "t");
        catalog.AddColumn(new App.Domain.Catalog.ColumnCatalogEntry
        {
            TableName = TableNames.DataSource,
            ColumnName = "app_desc",
            Kind = App.Domain.Catalog.ColumnKind.Computed,
            Formula = "lookup(name_key, application.app_code, description)",
        });
        catalog.AddColumn(new App.Domain.Catalog.ColumnCatalogEntry
        {
            TableName = TableNames.DataSource,
            ColumnName = "name_key",
        });
        Guid rowId = Guid.NewGuid();
        store.Upsert(TableNames.DataSource, new Row(TableNames.DataSource, rowId) { ["name"] = "X", ["name_key"] = "GDM1" }, "s", "t");

        ReferenceService svc = new(catalog, store);
        Row row = store.GetById(TableNames.DataSource, rowId)!;

        Assert.Equal("Core banking", svc.Evaluate(TableNames.DataSource, "app_desc", row));
        IReadOnlyDictionary<Guid, string?> bulk = svc.EvaluateColumn(TableNames.DataSource, "app_desc", [row]);
        Assert.Equal("Core banking", bulk[rowId]);
    }

    [Fact]
    public void Display_column_from_table_metadata_overrides_the_built_in_map()
    {
        (FakeCatalog catalog, FakeLocalStore store) = Seeded();
        store.Upsert(TableNames.Application, new Row(TableNames.Application, AppId) { ["app_code"] = "GDM1", ["description"] = "Core banking" }, "s", "t");
        FakeTableCatalog tables = new([new App.Domain.Catalog.TableCatalogEntry { TableName = TableNames.Application, DisplayColumn = "description" }]);

        ReferenceService svc = new(catalog, store, tables);

        Assert.Equal("Core banking", svc.Display(TableNames.Application, AppId.ToString()));
        Assert.Contains(svc.Options(TableNames.Application), o => o.Display == "Core banking");
    }

    [Fact]
    public void EvaluateColumn_counts_children_for_many_rows_in_one_pass()
    {
        (FakeCatalog catalog, FakeLocalStore store) = Seeded();
        ReferenceService svc = new(catalog, store);
        IReadOnlyList<Row> sources = store.GetAll(TableNames.DataSource);

        IReadOnlyDictionary<Guid, string?> counts = svc.EvaluateColumn(TableNames.DataSource, "field_count", sources);

        Assert.Equal("2", counts[SourceId]); // matches the per-row Evaluate result
    }

    [Fact]
    public void EvaluateColumn_lookup_resolves_for_many_rows()
    {
        (FakeCatalog catalog, FakeLocalStore store) = Seeded();
        ReferenceService svc = new(catalog, store);
        Row withClass = new(TableNames.DictionaryEntry, Guid.NewGuid()) { ["classification_id"] = ClassId.ToString() };
        Row withoutClass = new(TableNames.DictionaryEntry, Guid.NewGuid());

        IReadOnlyDictionary<Guid, string?> values =
            svc.EvaluateColumn(TableNames.DictionaryEntry, "access_901", [withClass, withoutClass]);

        Assert.Equal("Open", values[withClass.Id]);
        Assert.Null(values[withoutClass.Id]);
    }

    [Fact]
    public void Grid_renders_reference_columns_as_pickers()
    {
        (FakeCatalog catalog, FakeLocalStore store) = Seeded();
        Services.AddSingleton<ICatalog>(catalog);
        Services.AddSingleton<ILocalStore>(store);
        Services.AddSingleton<LanguageState>();
        Services.AddSingleton<ReferenceService>();
        Services.AddSingleton<IReferenceResolver>(sp => sp.GetRequiredService<ReferenceService>());
        Services.AddSingleton<IComputedEvaluator>(sp => sp.GetRequiredService<ReferenceService>());

        var cut = Render<MetadataGrid>(p => p.Add(c => c.Table, TableNames.DataSource));

        Assert.Contains("<select", cut.Markup);   // application_id reference picker
        Assert.Contains("GDM1", cut.Markup);        // the referenced option's display
        Assert.Contains("NB fields", cut.Markup);   // computed column header
        Assert.Contains(">2<", cut.Markup);         // precomputed field_count value for the seeded source
    }
}
