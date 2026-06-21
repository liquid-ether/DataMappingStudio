using App.Application.Sync;

namespace App.Application.Tests.Sync;

public class AutoRefreshPlannerTests
{
    private static readonly Guid Row = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private readonly AutoRefreshPlanner _planner = new();

    private static FoldedState State(string name, string city)
    {
        FoldedState state = new();
        state.Table("t").Rows[Row] = new FoldedRow(Row) { Values = { ["name"] = name, ["city"] = city } };
        return state;
    }

    [Fact]
    public void Fast_forwards_cells_not_edited_locally()
    {
        FoldedState known = State("Alice", "Paris");
        FoldedState remote = State("Alice", "Lyon"); // city changed remotely

        RefreshPlan plan = _planner.Plan(locallyEditedCells: new HashSet<CellKey>(), known, remote);

        CellChange applied = Assert.Single(plan.Applied);
        Assert.Equal("city", applied.Cell.Column);
        Assert.Equal("Lyon", applied.Value);
        Assert.Empty(plan.Flagged);
    }

    [Fact]
    public void Flags_but_never_clobbers_locally_edited_cells()
    {
        FoldedState known = State("Alice", "Paris");
        FoldedState remote = State("Bob", "Paris"); // name changed remotely
        HashSet<CellKey> edited = [new CellKey("t", Row, "name")]; // and locally

        RefreshPlan plan = _planner.Plan(edited, known, remote);

        Assert.Empty(plan.Applied);
        Assert.Equal("name", Assert.Single(plan.Flagged).Column);
    }

    [Fact]
    public void Unchanged_cells_are_ignored()
    {
        FoldedState known = State("Alice", "Paris");
        FoldedState remote = State("Alice", "Paris");

        RefreshPlan plan = _planner.Plan(new HashSet<CellKey>(), known, remote);

        Assert.Empty(plan.Applied);
        Assert.Empty(plan.Flagged);
    }
}
