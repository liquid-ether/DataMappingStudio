namespace App.Domain.Entities;

/// <summary>
/// Privacy/access reference data (table <c>classification</c>) that dictionary entries point at.
/// Pass-through metadata only: the app stores and displays it for the downstream ETL engine but does
/// not interpret, enforce, or mask anything based on it (Architecture §1, §18 Q-U4 withdrawn).
/// </summary>
public sealed record Classification
{
    public Guid Id { get; init; }

    public required string Prp { get; init; }

    public string? Access901 { get; init; }

    public string? Disclosure902 { get; init; }

    public SyncMetadata Sync { get; init; } = SyncMetadata.New();
}
