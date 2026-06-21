using System.Text.RegularExpressions;

namespace App.Infrastructure.Local.Sqlite;

/// <summary>
/// Validates and quotes SQL identifiers. Catalog names are app-controlled, but quoting + a strict
/// whitelist keeps dynamic DDL/DML safe even for user-added column names.
/// </summary>
internal static partial class SqlIdentifier
{
    public static string Quote(string identifier)
    {
        if (!IsValid(identifier))
        {
            throw new ArgumentException($"Invalid SQL identifier '{identifier}'.", nameof(identifier));
        }

        return $"\"{identifier}\"";
    }

    public static bool IsValid(string? identifier)
        => !string.IsNullOrEmpty(identifier) && IdentifierPattern().IsMatch(identifier);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
