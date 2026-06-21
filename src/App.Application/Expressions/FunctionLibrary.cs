namespace App.Application.Expressions;

/// <summary>A predefined function exposed to the editor: name, signature and bilingual description.</summary>
public sealed record FunctionDefinition(string Name, string Signature, string DescriptionEn, string DescriptionFr);

/// <summary>
/// The predefined function library and keyword set (Architecture §7b: "predefined function library
/// defined as metadata → editor completion + validation"). Ports the mockup's <c>FUNCS</c>/<c>KEYWORDS</c>
/// and adds signatures/descriptions so it is extensible without code changes.
/// </summary>
public sealed class FunctionLibrary
{
    private static readonly FunctionDefinition[] Defs =
    [
        new("CONCAT", "CONCAT(value, …)", "Concatenates values into a string.", "Concatène des valeurs en chaîne."),
        new("IF", "IF(condition, then, else)", "Returns one value when true, another when false.", "Retourne une valeur si vrai, une autre si faux."),
        new("CASE", "CASE WHEN … THEN … ELSE … END", "Multi-branch conditional.", "Conditionnelle à plusieurs branches."),
        new("WHEN", "WHEN condition", "A CASE branch condition.", "Condition d'une branche CASE."),
        new("THEN", "THEN value", "A CASE branch result.", "Résultat d'une branche CASE."),
        new("ELSE", "ELSE value", "The CASE fallback.", "Valeur par défaut du CASE."),
        new("END", "END", "Ends a CASE expression.", "Termine une expression CASE."),
        new("COALESCE", "COALESCE(value, …)", "First non-null value.", "Première valeur non nulle."),
        new("NULLIF", "NULLIF(a, b)", "Null when a equals b, else a.", "Null si a égale b, sinon a."),
        new("TRIM", "TRIM(value)", "Removes leading/trailing spaces.", "Supprime les espaces de début/fin."),
        new("UPPER", "UPPER(value)", "Upper-cases text.", "Met le texte en majuscules."),
        new("LOWER", "LOWER(value)", "Lower-cases text.", "Met le texte en minuscules."),
        new("CAST", "CAST(value AS type)", "Converts a value's type.", "Convertit le type d'une valeur."),
        new("SUBSTR", "SUBSTR(value, start, length)", "Extracts a substring.", "Extrait une sous-chaîne."),
        new("LOOKUP", "LOOKUP(key, table, column)", "Looks up a value in a table.", "Recherche une valeur dans une table."),
        new("ROUND", "ROUND(value, digits)", "Rounds a number.", "Arrondit un nombre."),
        new("DATEDIFF", "DATEDIFF(unit, start, end)", "Difference between two dates.", "Écart entre deux dates."),
        new("CONTAINS", "CONTAINS(value, substring)", "True when value contains substring.", "Vrai si la valeur contient la sous-chaîne."),
    ];

    private static readonly string[] KeywordList = ["AND", "OR", "NOT", "IS", "NULL", "IN", "LIKE", "BETWEEN"];

    private readonly Dictionary<string, FunctionDefinition> _functions =
        Defs.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _keywords = new(KeywordList, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FunctionDefinition> Functions => Defs;

    public IReadOnlyList<string> Keywords => KeywordList;

    public bool IsFunction(string token) => _functions.ContainsKey(token);

    public bool IsKeyword(string token) => _keywords.Contains(token);

    public FunctionDefinition? Find(string name) => _functions.GetValueOrDefault(name);

    /// <summary>Functions whose names start with <paramref name="prefix"/> (for autocomplete).</summary>
    public IEnumerable<FunctionDefinition> Matching(string prefix)
        => Defs.Where(d => d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
