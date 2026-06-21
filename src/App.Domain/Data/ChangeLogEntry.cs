namespace App.Domain.Data;

/// <summary>How a field change came to be.</summary>
public enum ChangeOperation
{
    Insert = 0,
    Update = 1,
    Delete = 2,

    /// <summary>Produced by the Excel importer — distinct from manual edits in the audit trail (§10c).</summary>
    Import = 3,
}

/// <summary>
/// One published field change. This <em>is</em> the audit record (Architecture §8): the change log is
/// the audit log. The same tuple is appended to the analyst's own remote log and folded into the
/// canonical <c>audit_log</c> (Architecture §6a).
/// </summary>
public sealed record ChangeLogEntry
{
    public Guid ChangeId { get; init; }

    /// <summary>Groups all field changes made in one save/import as a single change set.</summary>
    public required string ChangeSetId { get; init; }

    public required string Table { get; init; }

    public Guid RowId { get; init; }

    public required string Column { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public ChangeOperation Operation { get; init; }

    public required string ChangedBy { get; init; }

    public DateTimeOffset ChangedAtUtc { get; init; }

    /// <summary>Per-writer monotonic sequence; the stable tiebreaker used by the fold (Architecture §6a).</summary>
    public long ClientSeq { get; init; }
}
