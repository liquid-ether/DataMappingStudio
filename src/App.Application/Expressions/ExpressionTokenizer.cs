using System.Text.RegularExpressions;

namespace App.Application.Expressions;

/// <summary>
/// Splits an expression into raw tokens. Direct port of the mockup's <c>tokens()</c> regex so the
/// engine and the UI agree exactly on token boundaries (strings, identifiers incl. <c>alias.field</c>,
/// multi/single-char operators, punctuation, whitespace).
/// </summary>
public static partial class ExpressionTokenizer
{
    public static IReadOnlyList<string> Tokenize(string expression)
    {
        if (string.IsNullOrEmpty(expression))
        {
            return [];
        }

        return TokenPattern().Matches(expression).Select(m => m.Value).ToList();
    }

    /// <summary>An identifier token referencing a field: bare <c>name</c> or qualified <c>alias.field</c>.</summary>
    public static bool IsIdentifier(string token) => IdentifierPattern().IsMatch(token);

    /// <summary>A qualified reference token <c>alias.field</c>.</summary>
    public static bool IsQualified(string token) => QualifiedPattern().IsMatch(token);

    // Adds a numeric-literal alternative the mockup regex omitted (it silently dropped numbers);
    // identifiers can't start with a digit so ordering is unambiguous.
    [GeneratedRegex(@"'[^']*'|\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)?|>=|<=|!=|[-+*/=<>]|[(),]|\s+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^[A-Za-z_]\w*(?:\.\w+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^[A-Za-z_]\w*\.\w+$", RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedPattern();
}
