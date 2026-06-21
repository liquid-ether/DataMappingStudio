using App.Domain.Expressions;

namespace App.Domain.Tests.Expressions;

public class RuleExpressionTests
{
    private static readonly Guid MrrEntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Mirrors the mockup's lifetime_value mapping: COALESCE(b.mrr, 0) * 12
    private static RuleExpression LifetimeValue()
    {
        ExpressionNode coalesce = new FunctionCallNode("COALESCE",
        [
            new FieldReferenceNode("b.mrr", MrrEntryId),
            new LiteralNode("0", LiteralKind.Number),
        ]);

        ExpressionNode root = new FunctionCallNode("*",
        [
            coalesce,
            new LiteralNode("12", LiteralKind.Number),
        ]);

        return new RuleExpression(root, "COALESCE(b.mrr,0) * 12");
    }

    [Fact]
    public void Round_trips_through_json()
    {
        RuleExpression original = LifetimeValue();

        string json = original.ToJson();
        RuleExpression restored = RuleExpression.FromJson(json);

        Assert.Equal(json, restored.ToJson());
        Assert.Equal(original.SourceText, restored.SourceText);
    }

    [Fact]
    public void Deserializes_to_the_expected_node_types()
    {
        RuleExpression restored = RuleExpression.FromJson(LifetimeValue().ToJson());

        FunctionCallNode root = Assert.IsType<FunctionCallNode>(restored.Root);
        Assert.Equal("*", root.Name);
        FunctionCallNode coalesce = Assert.IsType<FunctionCallNode>(root.Arguments[0]);
        Assert.Equal("COALESCE", coalesce.Name);
        FieldReferenceNode field = Assert.IsType<FieldReferenceNode>(coalesce.Arguments[0]);
        Assert.Equal(MrrEntryId, field.DictionaryEntryId);
        Assert.True(field.IsResolved);
    }

    [Fact]
    public void Field_references_are_enumerated_across_the_tree()
    {
        RuleExpression expr = LifetimeValue();

        Assert.Single(expr.FieldReferences());
    }

    [Fact]
    public void Unresolved_references_are_detected()
    {
        ExpressionNode root = new FunctionCallNode("UPPER",
        [
            new FieldReferenceNode("b.plan_label"), // no dictionary id -> unresolved
        ]);
        RuleExpression expr = new(root, "UPPER(b.plan_label)");

        Assert.True(expr.HasUnresolved);
        Assert.Single(expr.UnresolvedReferences());
    }

    [Fact]
    public void Raw_text_expression_round_trips_and_flags_unresolved()
    {
        RuleExpression expr = RuleExpression.RawText("some free text the parser did not understand");

        RuleExpression restored = RuleExpression.FromJson(expr.ToJson());

        Assert.IsType<RawTextNode>(restored.Root);
        Assert.True(restored.HasUnresolved);
    }
}
