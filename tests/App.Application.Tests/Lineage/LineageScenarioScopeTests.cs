using App.Application.Lineage;
using App.Domain.Entities;

namespace App.Application.Tests.Lineage;

public class LineageScenarioScopeTests
{
    // S ─▶ T1 ─▶ T2 ─▶ T3 ; T1 also has a sibling source OTHER.
    private static LineageScenario Build() => new(
        [
            new LineageTarget("T1", [LineageSource.Table("a", "a", "S", ["x", "y"]), LineageSource.Table("o", "b", "OTHER", ["z"])]),
            new LineageTarget("T2", [LineageSource.Target("u", "tsrc", "T1")]),
            new LineageTarget("T3", [LineageSource.Target("u", "tsrc", "T2")]),
        ],
        [
            new LineageRow(MappingKind.Field, "T1", "f1", "a.x"),
            new LineageRow(MappingKind.Field, "T1", "f2", "o.z"),
            new LineageRow(MappingKind.Field, "T2", "g", "u.f1"),
            new LineageRow(MappingKind.Field, "T3", "h", "u.g"),
        ]);

    [Fact]
    public void Source_names_lists_the_raw_sources_sorted()
    {
        Assert.Equal(["OTHER", "S"], Build().SourceNames());
    }

    [Fact]
    public void Scoping_to_a_source_includes_downstream_and_sibling_context()
    {
        LineageScenario scoped = Build().ScopedToSource("S");

        // Downstream impact: every target built (transitively) from S.
        Assert.Equal(["T1", "T2", "T3"], scoped.Targets.Select(t => t.Name).OrderBy(n => n));

        // Both-ways context: T1's other source (OTHER) still renders.
        LineageTarget t1 = scoped.Targets.Single(t => t.Name == "T1");
        Assert.Contains(t1.Sources, s => s.Name == "OTHER");
    }

    [Fact]
    public void Scoping_to_an_unused_source_is_empty()
    {
        Assert.Empty(Build().ScopedToSource("NOPE").Targets);
    }
}
