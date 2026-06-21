using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Domain.Expressions;

/// <summary>
/// A complete expression: the AST <see cref="Root"/> plus the original <see cref="SourceText"/>.
/// Stored as "serialized JSON expression tree + original text" (Architecture §7b). Named
/// <c>RuleExpression</c> to avoid colliding with <see cref="System.Linq.Expressions.Expression"/>.
/// </summary>
public sealed record RuleExpression(ExpressionNode Root, string SourceText)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>An expression that is entirely unparsed free text.</summary>
    public static RuleExpression RawText(string text) => new(new RawTextNode(text), text);

    /// <summary>Serializes the whole expression (tree + source) to JSON for storage.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserializes an expression previously produced by <see cref="ToJson"/>.</summary>
    public static RuleExpression FromJson(string json)
        => JsonSerializer.Deserialize<RuleExpression>(json, JsonOptions)
           ?? throw new FormatException("Expression JSON deserialized to null.");

    /// <summary>All field references in the tree (resolved and unresolved).</summary>
    public IEnumerable<FieldReferenceNode> FieldReferences()
        => Root.DescendantsAndSelf().OfType<FieldReferenceNode>();

    /// <summary>Field references that are not bound to a dictionary entry — surfaced for cleanup.</summary>
    public IEnumerable<FieldReferenceNode> UnresolvedReferences()
        => FieldReferences().Where(static r => !r.IsResolved);

    /// <summary>
    /// True when the whole expression is unparsed free text (a <see cref="RawTextNode"/> root) or any
    /// field reference is unresolved. Inner raw fragments (operators, punctuation) do not count.
    /// </summary>
    public bool HasUnresolved => Root is RawTextNode || UnresolvedReferences().Any();
}
