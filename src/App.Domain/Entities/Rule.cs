using App.Domain.Expressions;

namespace App.Domain.Entities;

/// <summary>
/// A named, reusable expression (table <c>rule</c>). The workbook has no Rules sheet — rules are
/// referenced inside Mapping; our model promotes them to first-class reusable expressions (§7b).
/// </summary>
public sealed record Rule
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required RuleExpression Expression { get; init; }

    public string? Description { get; init; }

    public SyncMetadata Sync { get; init; } = SyncMetadata.New();

    public GovernanceMetadata Governance { get; init; }
}
