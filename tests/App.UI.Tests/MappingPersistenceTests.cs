using App.Application.Mappings;
using App.Domain.Entities;
using App.UI.MappingStudio;

namespace App.UI.Tests;

public class MappingPersistenceTests
{
    private static MappingStudioState Studio(FakeLocalStore store)
        => new(new MappingRepository(store), new MappingTargetRepository(store));

    [Fact]
    public void Repository_round_trips_a_mapping_record()
    {
        FakeLocalStore store = new();
        MappingRepository repo = new(store);
        Guid id = Guid.NewGuid();

        repo.Save(new MappingRecord(id, "CUSTOMER_360", "email", MappingKind.Field, "text", "a.email"), "alice");

        MappingRecord loaded = Assert.Single(repo.GetAll());
        Assert.Equal(id, loaded.Id);
        Assert.Equal("email", loaded.Field);
        Assert.Equal(MappingKind.Field, loaded.Kind);
        Assert.Equal("a.email", loaded.Expression);
        // The write went through the local store (so it becomes a change-log entry).
        Assert.Equal("a.email", store.GetById(TableNames.Mapping, id)!["expression"]);
    }

    [Fact]
    public void Repository_update_and_delete()
    {
        FakeLocalStore store = new();
        MappingRepository repo = new(store);
        Guid id = Guid.NewGuid();
        repo.Save(new MappingRecord(id, "T", "f", MappingKind.Field, "text", "a.x"), "alice");

        repo.Save(new MappingRecord(id, "T", "f", MappingKind.Calc, "number", "a.y"), "alice");
        MappingRecord updated = Assert.Single(repo.GetAll());
        Assert.Equal(MappingKind.Calc, updated.Kind);
        Assert.Equal("a.y", updated.Expression);

        repo.Delete(id, "alice");
        Assert.Empty(repo.GetAll());
    }

    [Fact]
    public void Studio_seeds_the_demo_on_first_load_and_persists_it()
    {
        FakeLocalStore store = new();
        MappingRepository repo = new(store);

        MappingStudioState studio = Studio(store);
        _ = studio.Rows; // triggers load + seed-if-empty

        Assert.Equal(15, repo.GetAll().Count); // demo persisted to the store
    }

    [Fact]
    public void Edits_persist_across_studio_instances()
    {
        FakeLocalStore store = new();

        MappingStudioState first = Studio(store);
        int seeded = first.Rows.Count;
        first.AddRow(MappingKind.Field, "CUSTOMER_360");

        // A fresh studio over the same store loads the persisted rows (no re-seed).
        MappingStudioState second = Studio(store);
        Assert.Equal(seeded + 1, second.Rows.Count);
    }

    [Fact]
    public void Target_source_alias_structure_persists_and_round_trips()
    {
        FakeLocalStore store = new();
        _ = Studio(store).Targets; // seeds targets + sources to the store

        MappingStudioState reloaded = Studio(store);
        App.Application.Lineage.LineageTarget customer = reloaded.Targets.Single(t => t.Name == "CUSTOMER_360");

        Assert.Equal(2, customer.Sources.Count);
        Assert.Contains(customer.Sources, s => s.Alias == "a" && s.Name == "CRM_ACCOUNTS" && s.Fields.Contains("acct_id"));
        Assert.True(reloaded.Targets.Single(t => t.Name == "MARKETING_SEGMENTS").Sources.Single().IsTarget);
    }

    [Fact]
    public void In_place_edit_is_written_through_to_the_store()
    {
        FakeLocalStore store = new();
        MappingStudioState studio = Studio(store);
        MappingRow row = studio.Rows.First(r => r.Field == "email");

        row.Expression = "UPPER(a.email)";
        studio.Save(row);

        MappingStudioState reloaded = Studio(store);
        Assert.Equal("UPPER(a.email)", reloaded.Rows.Single(r => r.Id == row.Id).Expression);
    }
}
