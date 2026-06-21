using App.Domain.Expressions;

namespace App.Domain.Entities;

/// <summary>The kind of a mapping row, mirroring the mockup's grid (join / filter / field / calculated).</summary>
public enum MappingKind
{
    Field = 0,
    Calc = 1,
    Join = 2,
    Filter = 3,
}

/// <summary>
/// A mapping (table <c>mapping</c>), kept in our expression model rather than the workbook's
/// denormalized shape. Only core fields are stored; every "used/shown" column (sources used,
/// source columns, etc.) is derived by the expression + lineage engine and never stored (§4 / Appendix A).
/// </summary>
public sealed record Mapping
{
    public Guid Id { get; init; }

    /// <summary>Target dictionary entry this row produces (store the Id, show its unique key).</summary>
    public Guid TargetEntryId { get; init; }

    /// <summary>Optional reusable rule applied by this mapping.</summary>
    public Guid? RuleId { get; init; }

    public MappingKind Kind { get; init; } = MappingKind.Field;

    /// <summary>The transformation expression ("Cible - Règles de transformation").</summary>
    public required RuleExpression Expression { get; init; }

    public bool IsTokenized { get; init; }

    public bool IsTokenizedPostTransformation { get; init; }

    public SyncMetadata Sync { get; init; } = SyncMetadata.New();

    public GovernanceMetadata Governance { get; init; }

    /// <summary>Join and filter rows do not produce a target field.</summary>
    public bool ProducesField => Kind is MappingKind.Field or MappingKind.Calc;
}
