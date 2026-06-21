namespace App.Domain.Entities;

/// <summary>
/// Uniform governance stamp carried by every domain row (Architecture §4 normalization, principle 5):
/// status (a lookup), revision date/reference, and free-text notes — named identically everywhere.
/// </summary>
public readonly record struct GovernanceMetadata(
    Guid? StatusLookupId,
    DateTimeOffset? RevisedAt,
    string? RevisionRef,
    string? Notes)
{
    public static GovernanceMetadata None => default;
}
