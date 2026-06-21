using App.Application.Sync;
using App.Domain.Data;
using static App.Application.Tests.Sync.SyncTestData;

namespace App.Application.Tests.Sync;

public class ChangeFoldTests
{
    private static readonly Guid Row = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Fold_is_deterministic_regardless_of_input_order()
    {
        List<ChangeLogEntry> entries =
        [
            Entry("t", Row, "name", null, "Alice", "a", 1, 0),
            Entry("t", Row, "name", "Alice", "Bob", "b", 1, 5),
            Entry("t", Row, "city", null, "Paris", "a", 2, 2),
        ];

        SortedDictionary<string, string?> forward = Cells(ChangeFold.Fold(entries));
        SortedDictionary<string, string?> reversed = Cells(ChangeFold.Fold(Enumerable.Reverse(entries)));

        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Last_write_per_cell_wins_by_timestamp()
    {
        List<ChangeLogEntry> entries =
        [
            Entry("t", Row, "name", null, "Alice", "a", 1, 0),
            Entry("t", Row, "name", "Alice", "Bob", "b", 1, 9), // later → wins
        ];

        FoldedState state = ChangeFold.Fold(entries);

        Assert.Equal("Bob", state.Value(new CellKey("t", Row, "name")));
    }

    [Fact]
    public void Delete_marks_row_deleted_and_excludes_it()
    {
        List<ChangeLogEntry> entries =
        [
            Entry("t", Row, "name", null, "Alice", "a", 1, 0, ChangeOperation.Insert),
            Entry("t", Row, SyncColumns.IsDeleted, "false", "true", "a", 2, 1, ChangeOperation.Delete),
        ];

        FoldedState state = ChangeFold.Fold(entries);

        Assert.True(state.Table("t").Rows[Row].IsDeleted);
        Assert.Empty(Cells(state)); // deleted rows excluded from AllCells
    }
}
