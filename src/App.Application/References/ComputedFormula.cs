using System.Text.RegularExpressions;
using App.Application.Abstractions;
using App.Domain.Catalog;

namespace App.Application.References;

/// <summary>
/// The evaluable computed-column grammar, parsed once and shared by the evaluator
/// (<see cref="ReferenceService"/>) and the admin formula builder's validation:
/// <list type="bullet">
///   <item><c>count(child_table.fk_column)</c> — how many child rows reference this row.</item>
///   <item><c>lookup(ref_column.target_column)</c> — follow this table's Reference column, return a
///     column from the referenced row.</item>
///   <item><c>lookup(key_column, target_table, return_column)</c> — match this row's key value against
///     the target table's <b>display column</b> (or an explicit one via
///     <c>lookup(key, target_table.match_column, return_column)</c>), return a column from the match.
///     The 3-argument form is the <c>LOOKUP(key, table, column)</c> signature the expression help
///     already advertises.</item>
/// </list>
/// </summary>
public abstract partial record ComputedFormula
{
    public sealed record Count(string ChildTable, string FkColumn) : ComputedFormula;

    public sealed record RefLookup(string RefColumn, string TargetColumn) : ComputedFormula;

    public sealed record KeyLookup(string KeyColumn, string TargetTable, string? MatchColumn, string ReturnColumn) : ComputedFormula;

    public static ComputedFormula? Parse(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            return null;
        }

        Match count = CountPattern().Match(formula);
        if (count.Success)
        {
            return new Count(count.Groups["table"].Value, count.Groups["col"].Value);
        }

        Match refLookup = RefLookupPattern().Match(formula);
        if (refLookup.Success)
        {
            return new RefLookup(refLookup.Groups["ref"].Value, refLookup.Groups["col"].Value);
        }

        Match keyLookup = KeyLookupPattern().Match(formula);
        if (keyLookup.Success)
        {
            return new KeyLookup(
                keyLookup.Groups["key"].Value,
                keyLookup.Groups["table"].Value,
                keyLookup.Groups["match"] is { Success: true } m ? m.Value : null,
                keyLookup.Groups["ret"].Value);
        }

        return null;
    }

    /// <summary>
    /// Human-readable validation for the admin builder: does the formula parse, and do the tables and
    /// columns it names exist (with the right kinds)? Empty list = valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(string table, string? formula, ICatalog catalog, ITableCatalog? tables = null)
    {
        List<string> errors = [];
        ComputedFormula? parsed = Parse(formula);
        if (parsed is null)
        {
            errors.Add("Unrecognized formula. Supported: count(child_table.fk_column), lookup(ref_column.target_column), lookup(key_column, target_table, return_column).");
            return errors;
        }

        HashSet<string> knownTables = catalog.GetTables().ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<ColumnCatalogEntry> ownColumns = catalog.GetForTable(table);

        switch (parsed)
        {
            case Count(var childTable, var fkColumn):
                if (!knownTables.Contains(childTable))
                {
                    errors.Add($"Unknown table '{childTable}'.");
                }
                else if (!catalog.GetForTable(childTable).Any(c => c.ColumnName == fkColumn))
                {
                    errors.Add($"Table '{childTable}' has no column '{fkColumn}'.");
                }

                break;

            case RefLookup(var refColumn, var targetColumn):
                ColumnCatalogEntry? reference = ownColumns.FirstOrDefault(c => c.ColumnName == refColumn);
                if (reference is null)
                {
                    errors.Add($"'{table}' has no column '{refColumn}'.");
                }
                else if (reference.Kind != ColumnKind.Reference || reference.ReferenceTarget is null)
                {
                    errors.Add($"'{refColumn}' is not a reference column.");
                }
                else if (!catalog.GetForTable(reference.ReferenceTarget).Any(c => c.ColumnName == targetColumn))
                {
                    errors.Add($"Table '{reference.ReferenceTarget}' has no column '{targetColumn}'.");
                }

                break;

            case KeyLookup(var keyColumn, var targetTable, var matchColumn, var returnColumn):
                if (!ownColumns.Any(c => c.ColumnName == keyColumn))
                {
                    errors.Add($"'{table}' has no column '{keyColumn}'.");
                }

                if (!knownTables.Contains(targetTable))
                {
                    errors.Add($"Unknown table '{targetTable}'.");
                    break;
                }

                IReadOnlyList<ColumnCatalogEntry> targetColumns = catalog.GetForTable(targetTable);
                if (!targetColumns.Any(c => c.ColumnName == returnColumn))
                {
                    errors.Add($"Table '{targetTable}' has no column '{returnColumn}'.");
                }

                if (matchColumn is not null)
                {
                    if (!targetColumns.Any(c => c.ColumnName == matchColumn))
                    {
                        errors.Add($"Table '{targetTable}' has no column '{matchColumn}'.");
                    }
                }
                else if (tables?.GetTableMeta(targetTable)?.DisplayColumn is null && !ReferenceService.HasDefaultDisplayColumn(targetTable))
                {
                    errors.Add($"Table '{targetTable}' has no display column — use lookup({keyColumn}, {targetTable}.match_column, {returnColumn}) to name one.");
                }

                break;
        }

        return errors;
    }

    [GeneratedRegex(@"^\s*count\(\s*(?<table>\w+)\.(?<col>\w+)\s*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CountPattern();

    [GeneratedRegex(@"^\s*lookup\(\s*(?<ref>\w+)\.(?<col>\w+)\s*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RefLookupPattern();

    [GeneratedRegex(@"^\s*lookup\(\s*(?<key>\w+)\s*,\s*(?<table>\w+)(?:\.(?<match>\w+))?\s*,\s*(?<ret>\w+)\s*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyLookupPattern();
}
