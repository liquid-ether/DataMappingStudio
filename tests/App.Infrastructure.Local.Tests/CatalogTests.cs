using App.Domain.Catalog;
using App.Domain.Data;

namespace App.Infrastructure.Local.Tests;

public class CatalogTests
{
    [Fact]
    public void Seed_is_idempotent()
    {
        using LocalStoreFixture fx = new();
        int before = fx.Catalog.GetForTable(LocalStoreFixture.Table).Count;

        fx.Catalog.Seed(LocalStoreFixture.SeedCatalog()); // seed the same entries again

        Assert.Equal(before, fx.Catalog.GetForTable(LocalStoreFixture.Table).Count);
    }

    [Fact]
    public void Bilingual_labels_round_trip()
    {
        using LocalStoreFixture fx = new();

        ColumnCatalogEntry name = fx.Catalog.GetForTable(LocalStoreFixture.Table).Single(e => e.ColumnName == "name");

        Assert.Equal("Name", name.LabelEn);
        Assert.Equal("Nom", name.LabelFr);
    }

    [Fact]
    public void Added_column_appears_in_reads_and_writes_without_code_change()
    {
        using LocalStoreFixture fx = new();
        Guid id = Guid.NewGuid();

        fx.Catalog.AddColumn(new ColumnCatalogEntry
        {
            TableName = LocalStoreFixture.Table,
            ColumnName = "color",
            ValueType = CatalogValueType.Text,
            LabelEn = "Color",
            LabelFr = "Couleur",
            IsUserAdded = true,
            DisplayOrder = 9,
        });

        Row row = new(LocalStoreFixture.Table, id) { ["name"] = "W", ["color"] = "red" };
        fx.Store.Upsert(LocalStoreFixture.Table, row, "cs1", "alice");

        Row read = fx.Store.GetById(LocalStoreFixture.Table, id)!;
        Assert.Equal("red", read["color"]);
        Assert.Contains(fx.Catalog.GetForTable(LocalStoreFixture.Table), e => e is { ColumnName: "color", IsUserAdded: true });
    }

    [Fact]
    public void GetTables_includes_tables_known_only_to_table_catalog()
    {
        using LocalStoreFixture fx = new();

        // A table whose columns haven't synced yet exists only in table_catalog — it must still be
        // listed (the wizard's table picker and the admin list both build on GetTables).
        fx.Catalog.UpsertTableMeta(new TableCatalogEntry { TableName = "orphan", LabelEn = "Orphan", IsUserAdded = true });

        Assert.Contains("orphan", fx.Catalog.GetTables());
        Assert.Contains(LocalStoreFixture.Table, fx.Catalog.GetTables());
    }

    [Fact]
    public void UpdateColumnMeta_changes_labels_only()
    {
        using LocalStoreFixture fx = new();
        ColumnCatalogEntry name = fx.Catalog.GetForTable(LocalStoreFixture.Table).Single(e => e.ColumnName == "name");

        fx.Catalog.UpdateColumnMeta(name with { LabelEn = "Widget name", LabelFr = "Nom du widget", IsRequired = !name.IsRequired });

        ColumnCatalogEntry updated = fx.Catalog.GetForTable(LocalStoreFixture.Table).Single(e => e.ColumnName == "name");
        Assert.Equal("Widget name", updated.LabelEn);
        Assert.Equal("Nom du widget", updated.LabelFr);
        Assert.Equal(name.IsRequired, updated.IsRequired); // presentation only — structure untouched
    }

    [Fact]
    public void Invalid_catalog_entry_is_rejected()
    {
        using LocalStoreFixture fx = new();

        Assert.Throws<ArgumentException>(() => fx.Catalog.AddColumn(new ColumnCatalogEntry
        {
            TableName = LocalStoreFixture.Table,
            ColumnName = "bad",
            Kind = ColumnKind.Reference, // reference without a target -> invalid
        }));
    }

    [Fact]
    public void Column_name_that_is_not_a_safe_identifier_is_rejected_without_persisting()
    {
        using LocalStoreFixture fx = new();
        int before = fx.Catalog.GetForTable(LocalStoreFixture.Table).Count;

        // A name with a space would pass the catalog INSERT but throw at ALTER TABLE — reject it first
        // so no phantom catalog row is left behind.
        Assert.Throws<ArgumentException>(() => fx.Catalog.AddColumn(new ColumnCatalogEntry
        {
            TableName = LocalStoreFixture.Table,
            ColumnName = "my col",
            ValueType = CatalogValueType.Text,
            LabelEn = "My col",
            LabelFr = "My col",
            IsUserAdded = true,
            DisplayOrder = 9,
        }));

        Assert.Equal(before, fx.Catalog.GetForTable(LocalStoreFixture.Table).Count);
        Assert.DoesNotContain(fx.Catalog.GetForTable(LocalStoreFixture.Table), e => e.ColumnName == "my col");
    }
}
