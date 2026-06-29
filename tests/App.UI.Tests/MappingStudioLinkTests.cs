using App.Application.Catalog;
using App.Application.Lineage;
using App.Application.Mappings;
using App.Domain.Data;
using App.Domain.Entities;

namespace App.UI.Tests;

/// <summary>
/// Verifies the Mapping Studio is linked to the real data model: a target's raw-source fields come from
/// the data source's dictionary entries (not the stored alias field list), and lineage scopes to a source.
/// </summary>
public class MappingStudioLinkTests
{
    private static (App.UI.MappingStudio.MappingStudioState State, FakeLocalStore Store) Build()
    {
        FakeLocalStore store = new();

        Row ds = new(TableNames.DataSource, Guid.NewGuid()) { ["name"] = "SRC_X" };
        store.Upsert(TableNames.DataSource, ds, "cs", "t");
        store.Upsert(TableNames.DictionaryEntry, new Row(TableNames.DictionaryEntry, Guid.NewGuid()) { ["source_id"] = ds.Id.ToString(), ["column_name"] = "real_col_1", ["ordinal"] = "1" }, "cs", "t");
        store.Upsert(TableNames.DictionaryEntry, new Row(TableNames.DictionaryEntry, Guid.NewGuid()) { ["source_id"] = ds.Id.ToString(), ["column_name"] = "real_col_2", ["ordinal"] = "2" }, "cs", "t");

        store.Upsert(TableNames.MappingTarget, new Row(TableNames.MappingTarget, Guid.NewGuid()) { ["name"] = "TG" }, "cs", "t");
        store.Upsert(TableNames.MappingSource, new Row(TableNames.MappingSource, Guid.NewGuid())
        {
            ["target"] = "TG",
            ["alias"] = "a",
            ["cls"] = "a",
            ["source_name"] = "SRC_X",
            ["is_target"] = "false",
            ["fields"] = "stored_only", // should be overridden by the real dictionary columns
        }, "cs", "t");
        store.Upsert(TableNames.Mapping, new Row(TableNames.Mapping, Guid.NewGuid()) { ["target"] = "TG", ["field"] = "f", ["kind"] = "Field", ["expression"] = "a.real_col_1" }, "cs", "t");

        var state = new App.UI.MappingStudio.MappingStudioState(new MappingRepository(store), new MappingTargetRepository(store), new CatalogQuery(store), new App.Application.Abstractions.EnvironmentCurrentUser());
        return (state, store);
    }

    [Fact]
    public void Raw_source_fields_come_from_the_dictionary_not_the_stored_alias_list()
    {
        (App.UI.MappingStudio.MappingStudioState state, _) = Build();

        LineageSource source = state.Targets.Single(t => t.Name == "TG").Sources.Single();

        Assert.Equal(["real_col_1", "real_col_2"], source.Fields);
        Assert.DoesNotContain("stored_only", source.Fields);
    }

    [Fact]
    public void Known_references_resolve_against_the_real_columns()
    {
        (App.UI.MappingStudio.MappingStudioState state, _) = Build();

        Assert.Contains(state.KnownReferences("TG"), r => r.Ref == "a.real_col_1");
    }

    [Fact]
    public void Data_sources_are_exposed_for_the_lineage_picker()
    {
        (App.UI.MappingStudio.MappingStudioState state, _) = Build();

        Assert.Contains(state.DataSources(), d => d.Name == "SRC_X");
        Assert.Contains("SRC_X", state.BuildScenario().SourceNames());
    }
}
