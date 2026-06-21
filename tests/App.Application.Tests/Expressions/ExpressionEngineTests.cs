using App.Application.Expressions;
using App.Domain.Expressions;

namespace App.Application.Tests.Expressions;

public class ExpressionEngineTests
{
    private static readonly FunctionLibrary Functions = new();
    private static readonly ExpressionClassifier Classifier = new(Functions);

    private static ResolutionContext Customer360Context()
        => new(MockupScenario.Build().KnownReferences("CUSTOMER_360"));

    [Fact]
    public void Function_library_recognizes_functions_and_keywords()
    {
        Assert.True(Functions.IsFunction("concat"));
        Assert.True(Functions.IsFunction("COALESCE"));
        Assert.True(Functions.IsKeyword("AND"));
        Assert.False(Functions.IsKeyword("CONCAT"));
        Assert.Equal("UPPER", Functions.Find("upper")!.Name);
        Assert.Contains(Functions.Matching("CO"), f => f.Name == "CONCAT");
        Assert.Contains(Functions.Matching("CO"), f => f.Name == "COALESCE");
    }

    [Fact]
    public void Classifies_functions_fields_and_literals()
    {
        IReadOnlyList<ClassifiedToken> tokens = Classifier.Classify("CONCAT(a.first_name,' ',a.last_name)", Customer360Context());

        Assert.Equal(ExpressionTokenType.Function, tokens[0].Type);
        ClassifiedToken firstName = tokens.Single(t => t.Text == "a.first_name");
        Assert.Equal(ExpressionTokenType.FieldReference, firstName.Type);
        Assert.Equal("a", firstName.Cls);
        Assert.Contains(tokens, t => t.Type == ExpressionTokenType.StringLiteral && t.Text == "' '");
    }

    [Fact]
    public void Self_field_reference_is_classified_as_self()
    {
        IReadOnlyList<ClassifiedToken> tokens = Classifier.Classify("IF(lifetime_value >= 1200, 1, 0)", Customer360Context());

        ClassifiedToken self = tokens.Single(t => t.Text == "lifetime_value");
        Assert.Equal(ExpressionTokenType.FieldReference, self.Type);
        Assert.Equal("self", self.Cls);
        Assert.Contains(tokens, t => t is { Type: ExpressionTokenType.NumberLiteral, Text: "1200" });
    }

    [Fact]
    public void Unknown_qualified_reference_is_unresolved()
    {
        Assert.Equal(1, Classifier.CountUnresolved("b.plan_label", Customer360Context()));
    }

    [Fact]
    public void Builder_links_resolved_field_references_and_round_trips()
    {
        Guid mrrId = Guid.NewGuid();
        ResolutionContext context = new([new KnownReference("b.mrr", "b", "b", "BILLING", mrrId)]);
        RuleExpressionBuilder builder = new(Functions, Classifier);

        RuleExpression expr = builder.Build("COALESCE(b.mrr,0) * 12", context);

        FieldReferenceNode bmrr = expr.FieldReferences().Single(r => r.Text == "b.mrr");
        Assert.Equal(mrrId, bmrr.DictionaryEntryId);
        Assert.False(expr.HasUnresolved);
        Assert.Equal(expr.ToJson(), RuleExpression.FromJson(expr.ToJson()).ToJson());
        Assert.Contains("COALESCE", builder.FunctionsUsed("COALESCE(b.mrr,0) * 12"));
    }

    [Fact]
    public void Builder_keeps_unresolved_reference_as_unlinked_and_flags_it()
    {
        RuleExpressionBuilder builder = new(Functions, Classifier);

        RuleExpression expr = builder.Build("b.plan_label", Customer360Context());

        Assert.True(expr.HasUnresolved);
        Assert.Single(expr.UnresolvedReferences());
    }

    [Fact]
    public void Builder_falls_back_to_raw_text_for_blank_input()
    {
        RuleExpressionBuilder builder = new(Functions, Classifier);

        Assert.IsType<RawTextNode>(builder.Build("   ", ResolutionContext.Empty).Root);
    }
}
