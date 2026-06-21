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
}
