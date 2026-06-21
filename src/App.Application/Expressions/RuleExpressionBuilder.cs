using App.Domain.Expressions;

namespace App.Application.Expressions;

/// <summary>
/// Builds a stored <see cref="RuleExpression"/> (AST + original text) from expression text. Field
/// tokens are linked to their dictionary entry via the resolution context so lineage is precise and
/// rename-safe; unresolved tokens are kept as unlinked field references (flagged, never blocking) and
/// truly free-form input round-trips as <see cref="RawTextNode"/> (Architecture §7b).
///
/// The AST is a flat, lossless token sequence wrapped in a synthetic <c>expr</c> call — sufficient as
/// the "exploitable format" (find all field refs / functions used) while the original text is always
/// preserved. A full precedence parser can replace this later without changing the stored contract.
/// </summary>
public sealed class RuleExpressionBuilder(FunctionLibrary functions, ExpressionClassifier classifier)
{
    public const string RootName = "expr";

    public RuleExpression Build(string expression, IResolutionContext context)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return RuleExpression.RawText(expression ?? string.Empty);
        }

        ResolutionContext resolver = context as ResolutionContext ?? new ResolutionContext(context.KnownReferences);
        List<ExpressionNode> nodes = [];

        foreach (ClassifiedToken token in classifier.Classify(expression, resolver))
        {
            switch (token.Type)
            {
                case ExpressionTokenType.Whitespace:
                    break;

                case ExpressionTokenType.StringLiteral:
                    nodes.Add(new LiteralNode(token.Text.Trim('\''), LiteralKind.String));
                    break;

                case ExpressionTokenType.NumberLiteral:
                    nodes.Add(new LiteralNode(token.Text, LiteralKind.Number));
                    break;

                case ExpressionTokenType.Function:
                    nodes.Add(new FunctionCallNode(token.Text, []));
                    break;

                case ExpressionTokenType.FieldReference:
                    nodes.Add(new FieldReferenceNode(token.Text, resolver.Resolve(token.Text)?.EntryId));
                    break;

                case ExpressionTokenType.Unresolved:
                    nodes.Add(new FieldReferenceNode(token.Text));
                    break;

                default: // Operator, Punctuation, Keyword, Identifier
                    nodes.Add(new RawTextNode(token.Text));
                    break;
            }
        }

        return new RuleExpression(new FunctionCallNode(RootName, nodes), expression);
    }

    /// <summary>The function names actually used in an expression (for validation/usage reporting).</summary>
    public IReadOnlyList<string> FunctionsUsed(string expression)
        => ExpressionTokenizer.Tokenize(expression)
            .Where(functions.IsFunction)
            .Select(t => functions.Find(t)!.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
