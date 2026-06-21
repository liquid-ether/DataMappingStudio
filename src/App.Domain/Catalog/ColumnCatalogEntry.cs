namespace App.Domain.Catalog;

/// <summary>
/// Describes a single column of a single table. The column catalog (the set of these entries)
/// drives the generic SQLite store and the metadata-driven UI editors, so a new column appears with
/// no code change (Architecture §4). Folded the same way as any other table.
/// </summary>
public sealed record ColumnCatalogEntry
{
    public required string TableName { get; init; }

    public required string ColumnName { get; init; }

    public ColumnKind Kind { get; init; } = ColumnKind.Scalar;

    public CatalogValueType ValueType { get; init; } = CatalogValueType.Text;

    public int? MaxLength { get; init; }

    /// <summary>French/business label (the original workbook wording lives here, not in the name).</summary>
    public string? LabelFr { get; init; }

    /// <summary>English label.</summary>
    public string? LabelEn { get; init; }

    public bool IsRequired { get; init; }

    /// <summary>Fixed core column the app relies on (IDs, FKs, sync metadata) — not user-editable.</summary>
    public bool IsCore { get; init; }

    /// <summary>Added by an analyst at runtime (via <c>ALTER TABLE … ADD COLUMN</c>).</summary>
    public bool IsUserAdded { get; init; }

    /// <summary>For <see cref="ColumnKind.Reference"/>: the target table this column links into.</summary>
    public string? ReferenceTarget { get; init; }

    /// <summary>For <see cref="ColumnKind.Computed"/>: the stored formula evaluated by the rule engine.</summary>
    public string? Formula { get; init; }

    public string? DefaultValue { get; init; }

    public int DisplayOrder { get; init; }

    /// <summary>Convenience view of <see cref="ValueType"/> + <see cref="MaxLength"/>.</summary>
    public ColumnType Type => new(ValueType, MaxLength);

    /// <summary>
    /// Validates the entry's invariants. Returns an empty list when valid. Used by catalog seeding
    /// and the add-column flow so the catalog can never describe a contradictory column.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(TableName))
        {
            errors.Add("TableName is required.");
        }

        if (string.IsNullOrWhiteSpace(ColumnName))
        {
            errors.Add("ColumnName is required.");
        }

        if (Kind == ColumnKind.Reference && string.IsNullOrWhiteSpace(ReferenceTarget))
        {
            errors.Add("Reference columns must specify a ReferenceTarget.");
        }

        if (Kind == ColumnKind.Computed && string.IsNullOrWhiteSpace(Formula))
        {
            errors.Add("Computed columns must specify a Formula.");
        }

        if (Kind != ColumnKind.Reference && !string.IsNullOrWhiteSpace(ReferenceTarget))
        {
            errors.Add("Only reference columns may specify a ReferenceTarget.");
        }

        if (MaxLength is < 1)
        {
            errors.Add("MaxLength, when set, must be positive.");
        }

        return errors;
    }

    public bool IsValid => Validate().Count == 0;
}
