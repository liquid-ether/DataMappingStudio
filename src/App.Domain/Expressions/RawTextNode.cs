namespace App.Domain.Expressions;

/// <summary>
/// Unparsed free text. Free-text expressions are always allowed: anything the parser cannot
/// interpret is stored verbatim and flagged, never blocking a save (Architecture §7b). Imported
/// expressions also fall back to this when field tokens cannot be resolved.
/// </summary>
public sealed record RawTextNode(string Text) : ExpressionNode;
