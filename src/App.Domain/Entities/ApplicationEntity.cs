namespace App.Domain.Entities;

/// <summary>
/// An application/system registry entry (workbook "Apps", table <c>application</c>). Named
/// <c>ApplicationEntity</c> to avoid colliding with the <c>App.Application</c> layer namespace.
/// </summary>
public sealed record ApplicationEntity
{
    public Guid Id { get; init; }

    public required string AppCode { get; init; }

    public string? NameGdm { get; init; }

    public string? NameVa360 { get; init; }

    public string? ShortName { get; init; }

    public string? GoldRootPath { get; init; }

    public string? Description { get; init; }

    public string? ResponsibleIt { get; init; }

    public string? ResponsibleItDelegate { get; init; }

    public string? ResponsibleBusiness { get; init; }

    public string? ResponsibleBusinessDelegate { get; init; }

    public SyncMetadata Sync { get; init; } = SyncMetadata.New();

    public GovernanceMetadata Governance { get; init; }
}
