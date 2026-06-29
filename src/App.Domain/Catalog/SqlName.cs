using System.Text.RegularExpressions;

namespace App.Domain.Catalog;

/// <summary>
/// The rule for a safe SQL identifier (table / column name): a letter or underscore, then letters,
/// digits or underscores. Shared so the UI can validate a user-entered column name up front and the
/// store can reject it before any half-applied change — the same rule the store uses when quoting
/// identifiers for dynamic DDL/DML.
/// </summary>
public static partial class SqlName
{
    public static bool IsValidIdentifier(string? name)
        => !string.IsNullOrEmpty(name) && IdentifierPattern().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
