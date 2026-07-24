namespace App.Domain.Catalog;

/// <summary>The kind of meta-model change carried by a <see cref="CatalogChangeEntry"/>.</summary>
public enum CatalogChangeKind
{
    /// <summary>A new table (its metadata rides in <see cref="CatalogChangeEntry.Table"/>).</summary>
    TableAdded = 0,

    /// <summary>A new column (rides in <see cref="CatalogChangeEntry.Column"/>).</summary>
    ColumnAdded = 1,

    /// <summary>Updated table metadata — labels / navigation / display column (last-writer-wins).</summary>
    TableMetaUpdated = 2,

    /// <summary>Updated column presentation metadata — labels (last-writer-wins; rides in <see cref="CatalogChangeEntry.Column"/>).</summary>
    ColumnMetaUpdated = 3,
}

/// <summary>
/// One meta-model change, appended to the author's per-writer catalog log in the shared folder
/// (<c>_meta/catalog/&lt;writer&gt;.json</c>) — the same only-the-owner-writes design as the data change
/// logs (Architecture §6a), so concurrent admins can never collide on a shared document. The fold of all
/// writers' entries (deterministic order, additive-only) is the team's runtime meta-model.
/// </summary>
public sealed record CatalogChangeEntry
{
    /// <summary>Per-writer monotone sequence (mirrors ChangeLogEntry.ClientSeq).</summary>
    public required long ClientSeq { get; init; }

    public required string ChangedBy { get; init; }

    public required DateTimeOffset ChangedAtUtc { get; init; }

    public required CatalogChangeKind Kind { get; init; }

    /// <summary>For <see cref="CatalogChangeKind.TableAdded"/> / <see cref="CatalogChangeKind.TableMetaUpdated"/>.</summary>
    public TableCatalogEntry? Table { get; init; }

    /// <summary>For <see cref="CatalogChangeKind.ColumnAdded"/>.</summary>
    public ColumnCatalogEntry? Column { get; init; }
}
