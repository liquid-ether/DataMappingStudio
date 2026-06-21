namespace App.Domain.Catalog;

/// <summary>
/// The role a catalog column plays. Scalar ships first (Phase 1); reference and computed columns
/// are reserved in the model from the start so later phases need no schema migration (Architecture §4).
/// </summary>
public enum ColumnKind
{
    /// <summary>A stored scalar value (text / number / date / bool / uuid …).</summary>
    Scalar = 0,

    /// <summary>A value that links to a row in another table (an FK; participates in lineage).</summary>
    Reference = 1,

    /// <summary>A value derived from a stored <see cref="ColumnCatalogEntry.Formula"/> via the rule engine.</summary>
    Computed = 2,
}
