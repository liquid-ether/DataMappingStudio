namespace App.Domain.Expressions;

/// <summary>The kind of a literal value in an expression.</summary>
public enum LiteralKind
{
    String = 0,
    Number = 1,
    Boolean = 2,
    Null = 3,
}

/// <summary>A literal value, e.g. <c>'ACTIVE'</c>, <c>12</c>, <c>true</c> (mockup: <c>.lit</c> tokens).</summary>
public sealed record LiteralNode(string Value, LiteralKind Kind) : ExpressionNode;
