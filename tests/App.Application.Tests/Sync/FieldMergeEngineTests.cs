using App.Application.Sync;
using static App.Application.Tests.Sync.SyncTestData;

namespace App.Application.Tests.Sync;

public class FieldMergeEngineTests
{
    private static readonly Guid Row = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly CellKey Cell = new("t", Row, "name");
    private readonly FieldMergeEngine _engine = new();

    private static FoldedState Remote(string? value)
    {
        FoldedState state = new();
        if (value is not null)
        {
            state.Table("t").Rows[Row] = new FoldedRow(Row) { Values = { ["name"] = value } };
        }

        return state;
    }

    private static Dictionary<CellKey, LocalEdit> Local(string? @base, string? local)
        => new() { [Cell] = new LocalEdit(@base, local) };

    [Fact]
    public void Local_change_with_unchanged_remote_is_clean()
    {
        MergeResult result = _engine.Merge(Local("x", "mine"), Remote("x"));

        Assert.False(result.HasConflicts);
        CellChange change = Assert.Single(result.CleanChanges);
        Assert.Equal("mine", change.Value);
    }

    [Fact]
    public void Divergent_remote_change_is_a_conflict()
    {
        MergeResult result = _engine.Merge(Local("x", "mine"), Remote("theirs"));

        Assert.Empty(result.CleanChanges);
        MergeConflict conflict = Assert.Single(result.Conflicts);
        Assert.Equal("x", conflict.Base);
        Assert.Equal("mine", conflict.Local);
        Assert.Equal("theirs", conflict.Remote);
    }

    [Fact]
    public void Converged_values_are_neither_clean_nor_conflict()
    {
        MergeResult result = _engine.Merge(Local("x", "same"), Remote("same"));

        Assert.Empty(result.CleanChanges);
        Assert.Empty(result.Conflicts);
    }

    [Theory]
    [InlineData(ConflictChoice.KeepMine, "mine")]
    [InlineData(ConflictChoice.KeepTheirs, "theirs")]
    public void Resolution_picks_the_chosen_side(ConflictChoice choice, string expected)
    {
        MergeConflict conflict = new(Cell, "x", "mine", "theirs");

        Assert.Equal(expected, _engine.Resolve(conflict, new ConflictResolution(Cell, choice)));
    }

    [Fact]
    public void Edit_resolution_uses_the_manual_value()
    {
        MergeConflict conflict = new(Cell, "x", "mine", "theirs");

        Assert.Equal("merged", _engine.Resolve(conflict, new ConflictResolution(Cell, ConflictChoice.Edit, "merged")));
    }
}
