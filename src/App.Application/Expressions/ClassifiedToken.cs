namespace App.Application.Expressions;

/// <summary>The visual/semantic category of an expression token (drives mockup-style highlighting).</summary>
public enum ExpressionTokenType
{
    Whitespace,
    StringLiteral,
    NumberLiteral,
    Operator,
    Punctuation,
    Function,
    Keyword,

    /// <summary>A reference resolved to a known field (rendered as a coloured chip).</summary>
    FieldReference,

    /// <summary>A <c>alias.field</c> reference that is not known in scope (rendered with a wavy underline).</summary>
    Unresolved,

    /// <summary>A bare identifier that is neither a known field, function nor keyword.</summary>
    Identifier,
}

/// <summary>
/// A token classified for rendering and analysis. <see cref="Cls"/> carries the chip colour class
/// (a/b/g/tsrc/self) for <see cref="ExpressionTokenType.FieldReference"/> tokens.
/// </summary>
public sealed record ClassifiedToken(string Text, ExpressionTokenType Type, string? Cls = null);
