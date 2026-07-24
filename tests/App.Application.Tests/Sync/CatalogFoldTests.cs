using App.Application.Sync;
using App.Domain.Catalog;

namespace App.Application.Tests.Sync;

public class CatalogFoldTests
{
    private static CatalogChangeEntry Table(string name, string by, long seq, int atMinutes, string? labelEn = null, CatalogChangeKind kind = CatalogChangeKind.TableAdded) => new()
    {
        ClientSeq = seq,
        ChangedBy = by,
        ChangedAtUtc = new DateTimeOffset(2026, 1, 1, 0, atMinutes, 0, TimeSpan.Zero),
        Kind = kind,
        Table = new TableCatalogEntry { TableName = name, LabelEn = labelEn ?? name, LabelFr = labelEn ?? name, IsUserAdded = true },
    };

    private static CatalogChangeEntry Column(string table, string column, string by, long seq, int atMinutes, CatalogValueType type = CatalogValueType.Text) => new()
    {
        ClientSeq = seq,
        ChangedBy = by,
        ChangedAtUtc = new DateTimeOffset(2026, 1, 1, 0, atMinutes, 0, TimeSpan.Zero),
        Kind = CatalogChangeKind.ColumnAdded,
        Column = new ColumnCatalogEntry { TableName = table, ColumnName = column, ValueType = type, IsUserAdded = true },
    };

    [Fact]
    public void Fold_is_deterministic_regardless_of_input_order()
    {
        CatalogChangeEntry[] entries = [Table("vendor", "alice", 1, 1), Column("vendor", "name", "alice", 2, 2), Column("vendor", "city", "bob", 1, 3)];

        FoldedCatalog forward = CatalogFold.Fold(entries);
        FoldedCatalog reversed = CatalogFold.Fold(entries.Reverse().ToArray());

        Assert.Equal(forward.Tables.Select(t => t.TableName), reversed.Tables.Select(t => t.TableName));
        Assert.Equal(forward.Columns.Select(c => (c.TableName, c.ColumnName)), reversed.Columns.Select(c => (c.TableName, c.ColumnName)));
    }

    [Fact]
    public void Structure_is_first_wins_and_never_deleted()
    {
        // Two writers add the same column with different types: the earlier one wins; nothing is removed.
        FoldedCatalog folded = CatalogFold.Fold(
        [
            Table("vendor", "alice", 1, 1),
            Column("vendor", "name", "bob", 1, 5, CatalogValueType.Integer),
            Column("vendor", "name", "alice", 2, 2, CatalogValueType.Text), // earlier
        ]);

        App.Domain.Catalog.ColumnCatalogEntry column = Assert.Single(folded.Columns);
        Assert.Equal(CatalogValueType.Text, column.ValueType);
        Assert.Single(folded.Tables);
    }

    [Fact]
    public void Table_metadata_is_last_writer_wins()
    {
        FoldedCatalog folded = CatalogFold.Fold(
        [
            Table("vendor", "alice", 1, 1, "Vendors"),
            Table("vendor", "bob", 1, 9, "Suppliers", CatalogChangeKind.TableMetaUpdated),
            Table("vendor", "carol", 1, 4, "Sellers", CatalogChangeKind.TableMetaUpdated),
        ]);

        Assert.Equal("Suppliers", Assert.Single(folded.Tables).LabelEn); // bob's is latest by time
    }

    [Fact]
    public void Ties_break_deterministically_by_writer_then_sequence()
    {
        // Same timestamp: writer order (ordinal) decides — "alice" applies before "bob", so bob's
        // TableMetaUpdated (later in the stable order) wins the LWW metadata.
        FoldedCatalog folded = CatalogFold.Fold(
        [
            Table("vendor", "bob", 1, 1, "FromBob", CatalogChangeKind.TableMetaUpdated),
            Table("vendor", "alice", 1, 1, "FromAlice", CatalogChangeKind.TableMetaUpdated),
        ]);

        Assert.Equal("FromBob", Assert.Single(folded.Tables).LabelEn);
    }

    [Fact]
    public void Column_metadata_is_last_writer_wins_and_additive()
    {
        static CatalogChangeEntry Meta(string labelEn, string by, long seq, int atMinutes) => new()
        {
            ClientSeq = seq,
            ChangedBy = by,
            ChangedAtUtc = new DateTimeOffset(2026, 1, 1, 0, atMinutes, 0, TimeSpan.Zero),
            Kind = CatalogChangeKind.ColumnMetaUpdated,
            Column = new ColumnCatalogEntry { TableName = "vendor", ColumnName = "name", ValueType = CatalogValueType.Text, LabelEn = labelEn, LabelFr = labelEn, IsUserAdded = true },
        };

        FoldedCatalog folded = CatalogFold.Fold(
        [
            Table("vendor", "alice", 1, 1),
            Column("vendor", "name", "alice", 2, 2),
            Meta("Supplier name", "bob", 1, 9),   // latest -> wins
            Meta("Vendor name", "carol", 1, 4),
        ]);

        ColumnCatalogEntry column = Assert.Single(folded.Columns);
        Assert.Equal("Supplier name", column.LabelEn);

        // A meta update for a column this copy never saw added still lands (additive, never deletes).
        FoldedCatalog metaOnly = CatalogFold.Fold([Meta("Orphaned label", "bob", 1, 1)]);
        Assert.Equal("Orphaned label", Assert.Single(metaOnly.Columns).LabelEn);
    }

    [Fact]
    public void Replaying_the_same_entries_twice_changes_nothing()
    {
        CatalogChangeEntry[] entries = [Table("vendor", "alice", 1, 1), Column("vendor", "name", "alice", 2, 2)];

        FoldedCatalog once = CatalogFold.Fold(entries);
        FoldedCatalog twice = CatalogFold.Fold([.. entries, .. entries]);

        Assert.Equal(once.Tables.Count, twice.Tables.Count);
        Assert.Equal(once.Columns.Count, twice.Columns.Count);
    }
}
