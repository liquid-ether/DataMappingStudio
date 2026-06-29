using App.Domain.Data;

namespace App.Application.References;

/// <summary>An option for a reference picker: the stored row id and its human-readable display value.</summary>
public sealed record ReferenceOption(Guid Id, string Display);

/// <summary>
/// Resolves reference (FK) columns to options and display values (Architecture §4, Phase 2). Reference
/// columns store the target row's id; the display value is shown live, never stored (avoids the Excel's
/// denormalization drift).
/// </summary>
public interface IReferenceResolver
{
    /// <summary>The selectable rows of <paramref name="targetTable"/> (id + display).</summary>
    IReadOnlyList<ReferenceOption> Options(string targetTable);

    /// <summary>The display value for a stored reference id (the id itself if unresolved).</summary>
    string? Display(string targetTable, string? id);
}

/// <summary>
/// Evaluates computed columns from the catalog <c>Formula</c> (Architecture §4). Supported forms:
/// <c>count(table.fk_column)</c> (children referencing this row) and <c>lookup(ref_column.target_column)</c>
/// (autofill from a referenced row). Computed values are read-only and recomputed on read.
/// </summary>
public interface IComputedEvaluator
{
    string? Evaluate(string table, string column, Row row);

    /// <summary>
    /// Evaluates a computed column for many rows at once, reading each referenced/child table only
    /// <em>once</em> instead of per row. Returns a row-id → value map. Prefer this over calling
    /// <see cref="Evaluate"/> in a loop (e.g. grids/reports over large tables): a <c>count(child.fk)</c>
    /// column would otherwise re-scan the whole child table for every parent row.
    /// </summary>
    IReadOnlyDictionary<Guid, string?> EvaluateColumn(string table, string column, IReadOnlyList<Row> rows);
}
