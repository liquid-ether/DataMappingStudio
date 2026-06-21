using System.Text.Json.Serialization;

namespace App.Domain.Expressions;

/// <summary>
/// Base of the rule/mapping expression AST — the "exploitable format" (Architecture §7b). The tree
/// serializes to JSON with a <c>kind</c> discriminator. Node types are intentionally minimal:
/// literals, (dictionary-bound) field references, function calls (also used for operators and SQL
/// constructs such as CASE/IN), and free-text fallback for anything not yet parsed.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LiteralNode), "literal")]
[JsonDerivedType(typeof(FieldReferenceNode), "field")]
[JsonDerivedType(typeof(FunctionCallNode), "call")]
[JsonDerivedType(typeof(RawTextNode), "raw")]
public abstract record ExpressionNode
{
    /// <summary>Depth-first walk over this node and its descendants (self first).</summary>
    public IEnumerable<ExpressionNode> DescendantsAndSelf()
    {
        yield return this;

        if (this is FunctionCallNode call)
        {
            foreach (ExpressionNode arg in call.Arguments)
            {
                foreach (ExpressionNode node in arg.DescendantsAndSelf())
                {
                    yield return node;
                }
            }
        }
    }
}
