using App.Application.Expressions;
using App.Application.Lineage;
using App.Domain.Lineage;

namespace App.Application.Tests.Lineage;

public class LineageEngineTests
{
    private static readonly LineageEngine Engine = new(new FunctionLibrary());

    private static string Nid(string ds, string field) => LineageNode.MakeId(ds, field);

    [Fact]
    public void Build_counts_the_single_unresolved_reference()
    {
        LineageGraph graph = Engine.Build(MockupScenario.Build());

        // b.plan_label is the only reference that resolves to no source column.
        Assert.Equal(1, graph.UnresolvedCount);
    }

    [Fact]
    public void Priority_lineage_traces_back_to_billing_mrr()
    {
        LineageScenario scenario = MockupScenario.Build();
        LineageGraph graph = Engine.Build(scenario);

        LineageClosure closure = Engine.UpstreamClosure(graph, Nid("MARKETING_SEGMENTS", "priority"));

        Assert.Contains(Nid("MARKETING_SEGMENTS", "priority"), closure.NodeIds);
        Assert.Contains(Nid("CUSTOMER_360", "risk_band"), closure.NodeIds);
        Assert.Contains(Nid("CUSTOMER_360", "is_high_value"), closure.NodeIds);
        Assert.Contains(Nid("CUSTOMER_360", "lifetime_value"), closure.NodeIds);
        Assert.Contains(Nid("BILLING", "mrr"), closure.NodeIds);
    }

    [Fact]
    public void Direct_source_field_traces_to_its_table_column()
    {
        LineageScenario scenario = MockupScenario.Build();
        LineageGraph graph = Engine.Build(scenario);

        LineageClosure closure = Engine.UpstreamClosure(graph, Nid("CUSTOMER_360", "customer_id"));

        Assert.Contains(Nid("CRM_ACCOUNTS", "acct_id"), closure.NodeIds);
    }

    [Fact]
    public void Built_from_tree_reaches_a_source_table_leaf()
    {
        LineageScenario scenario = MockupScenario.Build();
        LineageGraph graph = Engine.Build(scenario);

        LineageTreeNode? tree = Engine.BuiltFrom(scenario, graph, Nid("MARKETING_SEGMENTS", "priority"));

        Assert.NotNull(tree);
        Assert.Equal("priority", tree!.Field);
        Assert.True(HasSourceLeaf(tree, "BILLING", "mrr"));
    }

    [Fact]
    public void Source_table_nodes_are_marked_as_source_kind()
    {
        LineageGraph graph = Engine.Build(MockupScenario.Build());

        LineageNode mrr = graph.Nodes.Single(n => n.Id == Nid("BILLING", "mrr"));
        Assert.Equal(LineageNodeKind.SourceTable, mrr.Kind);

        LineageNode riskBand = graph.Nodes.Single(n => n.Id == Nid("CUSTOMER_360", "risk_band"));
        Assert.Equal(LineageNodeKind.Target, riskBand.Kind);
    }

    private static bool HasSourceLeaf(LineageTreeNode node, string dataSet, string field)
    {
        if (node is { IsSourceTable: true, IsLeaf: true } && node.DataSet == dataSet && node.Field == field)
        {
            return true;
        }

        return node.Children.Any(c => HasSourceLeaf(c, dataSet, field));
    }
}
