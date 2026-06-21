namespace App.Application.Expressions;

/// <summary>
/// Classifies expression tokens for highlighting and validation. Faithful port of the mockup's
/// <c>renderExpr()</c> token logic (literals → functions → keywords → known field chips → unresolved
/// <c>alias.field</c> → numbers → plain identifiers), driven by the <see cref="FunctionLibrary"/> and a
/// <see cref="IResolutionContext"/>.
/// </summary>
public sealed class ExpressionClassifier(FunctionLibrary functions)
{
    public IReadOnlyList<ClassifiedToken> Classify(string expression, IResolutionContext context)
    {
        ResolutionContext resolver = context as ResolutionContext ?? new ResolutionContext(context.KnownReferences);
        List<ClassifiedToken> result = [];

        foreach (string tk in ExpressionTokenizer.Tokenize(expression))
        {
            if (string.IsNullOrWhiteSpace(tk))
            {
                result.Add(new ClassifiedToken(tk, ExpressionTokenType.Whitespace));
                continue;
            }

            if (tk.Length >= 2 && tk[0] == '\'' && tk[^1] == '\'')
            {
                result.Add(new ClassifiedToken(tk, ExpressionTokenType.StringLiteral));
                continue;
            }

            if (IsPunctuation(tk))
            {
                result.Add(new ClassifiedToken(tk, IsParenOrComma(tk) ? ExpressionTokenType.Punctuation : ExpressionTokenType.Operator));
                continue;
            }

            if (functions.IsFunction(tk))
            {
                result.Add(new ClassifiedToken(tk, ExpressionTokenType.Function));
                continue;
            }

            if (functions.IsKeyword(tk))
            {
                result.Add(new ClassifiedToken(tk, ExpressionTokenType.Keyword));
                continue;
            }

            KnownReference? known = resolver.Resolve(tk);
            if (known is not null)
            {
                result.Add(new ClassifiedToken(tk, ExpressionTokenType.FieldReference, known.Cls));
                continue;
            }

            if (ExpressionTokenizer.IsQualified(tk))
            {
                result.Add(new ClassifiedToken(tk, ExpressionTokenType.Unresolved));
                continue;
            }

            if (char.IsDigit(tk[0]))
            {
                result.Add(new ClassifiedToken(tk, ExpressionTokenType.NumberLiteral));
                continue;
            }

            result.Add(new ClassifiedToken(tk, ExpressionTokenType.Identifier));
        }

        return result;
    }

    /// <summary>The number of unresolved <c>alias.field</c> references in the expression.</summary>
    public int CountUnresolved(string expression, IResolutionContext context)
        => Classify(expression, context).Count(t => t.Type == ExpressionTokenType.Unresolved);

    private static bool IsPunctuation(string tk)
        => IsParenOrComma(tk) || tk is ">=" or "<=" or "!=" or "+" or "-" or "*" or "/" or "=" or "<" or ">";

    private static bool IsParenOrComma(string tk) => tk is "(" or ")" or ",";
}
