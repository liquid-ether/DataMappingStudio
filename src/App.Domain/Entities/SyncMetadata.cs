namespace App.Domain.Entities;

/// <summary>
/// Sync metadata carried by every domain row (Diagrams, after the ER). <see cref="RowVersion"/> is a
/// local edit counter for fast unchanged-row skips; conflict detection itself is per-cell against the
/// analyst's last-known fold (<see cref="BaseVersion"/>), not this table-level counter (Architecture §6a/§7a).
/// </summary>
public readonly record struct SyncMetadata(
    long RowVersion,
    long BaseVersion,
    DateTimeOffset? ModifiedAt,
    string? ModifiedBy,
    bool IsDeleted)
{
    public static SyncMetadata New() => new(0, 0, null, null, false);
}
