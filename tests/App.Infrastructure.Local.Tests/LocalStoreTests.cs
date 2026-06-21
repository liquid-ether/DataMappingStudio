using App.Domain.Data;

namespace App.Infrastructure.Local.Tests;

public class LocalStoreTests
{
    private static Row Widget(Guid id, string name, string? qty = null, string? price = null, string? active = null)
    {
        Row row = new(LocalStoreFixture.Table, id) { ["name"] = name };
        if (qty is not null) { row["qty"] = qty; }
        if (price is not null) { row["price"] = price; }
        if (active is not null) { row["active"] = active; }
        return row;
    }

    [Fact]
    public void Insert_then_get_round_trips_values()
    {
        using LocalStoreFixture fx = new();
        Guid id = Guid.NewGuid();

        fx.Store.Upsert(LocalStoreFixture.Table, Widget(id, "Widget A", "5", "9.99", "true"), "cs1", "alice");

        Row? read = fx.Store.GetById(LocalStoreFixture.Table, id);
        Assert.NotNull(read);
        Assert.Equal("Widget A", read!["name"]);
        Assert.Equal("5", read["qty"]);
        Assert.Equal("9.99", read["price"]);
        Assert.Equal("true", read["active"]);
        Assert.Equal(1, read.RowVersion);
    }

    [Fact]
    public void Insert_logs_one_entry_per_non_null_field_as_insert()
    {
        using LocalStoreFixture fx = new();
        Guid id = Guid.NewGuid();

        IReadOnlyList<ChangeLogEntry> written =
            fx.Store.Upsert(LocalStoreFixture.Table, Widget(id, "Widget A", "5", "9.99", "true"), "cs1", "alice");

        Assert.Equal(4, written.Count);
        Assert.All(written, e => Assert.Equal(ChangeOperation.Insert, e.Operation));
        Assert.All(written, e => Assert.Null(e.OldValue));
        Assert.Contains(written, e => e is { Column: "price", NewValue: "9.99" });
    }

    [Fact]
    public void Update_logs_only_changed_fields_and_bumps_row_version()
    {
        using LocalStoreFixture fx = new();
        Guid id = Guid.NewGuid();
        fx.Store.Upsert(LocalStoreFixture.Table, Widget(id, "Widget A", "5", "9.99", "true"), "cs1", "alice");

        IReadOnlyList<ChangeLogEntry> written =
            fx.Store.Upsert(LocalStoreFixture.Table, Widget(id, "Widget A", "5", "19.98", "true"), "cs2", "bob");

        ChangeLogEntry change = Assert.Single(written);
        Assert.Equal("price", change.Column);
        Assert.Equal("9.99", change.OldValue);
        Assert.Equal("19.98", change.NewValue);
        Assert.Equal(ChangeOperation.Update, change.Operation);
        Assert.Equal(2, fx.Store.GetById(LocalStoreFixture.Table, id)!.RowVersion);
    }

    [Fact]
    public void Identical_update_writes_nothing()
    {
        using LocalStoreFixture fx = new();
        Guid id = Guid.NewGuid();
        fx.Store.Upsert(LocalStoreFixture.Table, Widget(id, "Widget A", "5"), "cs1", "alice");

        IReadOnlyList<ChangeLogEntry> written =
            fx.Store.Upsert(LocalStoreFixture.Table, Widget(id, "Widget A", "5"), "cs2", "alice");

        Assert.Empty(written);
        Assert.Equal(1, fx.Store.GetById(LocalStoreFixture.Table, id)!.RowVersion);
    }

    [Theory]
    [InlineData(" 5.00 ", "5")]
    [InlineData("Vrai", "true")]
    public void Values_are_normalized_on_write(string raw, string canonical)
    {
        using LocalStoreFixture fx = new();
        Guid id = Guid.NewGuid();
        // price and active exercise number + boolean normalization respectively.
        Row row = raw == "Vrai"
            ? Widget(id, "W", active: raw)
            : Widget(id, "W", price: raw);

        fx.Store.Upsert(LocalStoreFixture.Table, row, "cs1", "alice");

        Row read = fx.Store.GetById(LocalStoreFixture.Table, id)!;
        string column = raw == "Vrai" ? "active" : "price";
        Assert.Equal(canonical, read[column]);
    }

    [Fact]
    public void Unknown_column_is_rejected()
    {
        using LocalStoreFixture fx = new();
        Row row = new(LocalStoreFixture.Table, Guid.NewGuid()) { ["name"] = "W", ["nope"] = "x" };

        Assert.Throws<ArgumentException>(() => fx.Store.Upsert(LocalStoreFixture.Table, row, "cs1", "alice"));
    }

    [Fact]
    public void Soft_delete_hides_row_and_logs_a_delete()
    {
        using LocalStoreFixture fx = new();
        Guid id = Guid.NewGuid();
        fx.Store.Upsert(LocalStoreFixture.Table, Widget(id, "W"), "cs1", "alice");

        IReadOnlyList<ChangeLogEntry> written = fx.Store.SoftDelete(LocalStoreFixture.Table, id, "cs2", "alice");

        Assert.Equal(ChangeOperation.Delete, Assert.Single(written).Operation);
        Assert.Empty(fx.Store.GetAll(LocalStoreFixture.Table));
        Assert.Single(fx.Store.GetAll(LocalStoreFixture.Table, includeDeleted: true));
    }

    [Fact]
    public void Client_seq_is_monotonic_across_appends()
    {
        using LocalStoreFixture fx = new();
        fx.Store.Upsert(LocalStoreFixture.Table, Widget(Guid.NewGuid(), "A", "1"), "cs1", "alice");
        fx.Store.Upsert(LocalStoreFixture.Table, Widget(Guid.NewGuid(), "B", "2"), "cs2", "alice");

        IReadOnlyList<ChangeLogEntry> all = fx.Audit.Query();
        List<long> seqs = all.Select(e => e.ClientSeq).ToList();

        Assert.Equal(seqs.OrderBy(x => x), seqs);
        Assert.Equal(seqs.Distinct().Count(), seqs.Count);
    }
}
