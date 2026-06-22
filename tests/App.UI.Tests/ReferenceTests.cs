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

public class ReferenceTests : BunitContext
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
    }
}
