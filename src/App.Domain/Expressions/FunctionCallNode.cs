namespace App.Domain.Expressions;

/// <summary>
/// A function call such as <c>CONCAT(...)</c> or <c>COALESCE(...)</c>. Also represents operators
/// (e.g. <c>Name = "="</c>, <c>"AND"</c>) and SQL constructs (<c>CASE</c>, <c>IN</c>) so the AST can
/// capture the full expression grammar with a small set of node types.
/// </summary>
public sealed record FunctionCallNode(string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;
