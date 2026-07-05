namespace App.Domain.Catalog;

/// <summary>
/// Table-level metadata for the metadata-driven model: bilingual display labels, navigation placement,
/// and the column shown for this table in reference pickers. Persisted in the <c>table_catalog</c> meta
/// table beside <c>column_catalog</c>; the default entries come from <c>DefaultCatalog.TableMeta()</c>,
/// runtime tables add their own (<see cref="IsUserAdded"/>).
/// </summary>
public sealed record TableCatalogEntry
{
    public required string TableName { get; init; }

    public string? LabelEn { get; init; }

    public string? LabelFr { get; init; }

    /// <summary>Whether the table appears in the side navigation's Data model section.</summary>
    public bool NavVisible { get; init; }

    /// <summary>Position among nav-visible tables.</summary>
    public int NavOrder { get; init; }

    /// <summary>The column whose value represents a row in reference pickers (falls back to the id).</summary>
    public string? DisplayColumn { get; init; }

    /// <summary>Created at runtime through the meta-model admin (vs seeded defaults).</summary>
    public bool IsUserAdded { get; init; }

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (!SqlName.IsValidIdentifier(TableName))
        {
            errors.Add($"Invalid table name '{TableName}'. Use letters, digits and underscores, starting with a letter or underscore.");
        }

        if (DisplayColumn is not null && !SqlName.IsValidIdentifier(DisplayColumn))
        {
            errors.Add($"Invalid display column '{DisplayColumn}'.");
        }

        if (IsUserAdded && string.IsNullOrWhiteSpace(LabelEn) && string.IsNullOrWhiteSpace(LabelFr))
        {
            errors.Add("A user-added table needs at least one display label.");
        }

        return errors;
    }

    public bool IsValid => Validate().Count == 0;
}
