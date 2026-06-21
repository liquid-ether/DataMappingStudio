namespace App.Domain.Data;

/// <summary>
/// Names of the fixed core/sync columns every table carries. Shared so the local store, the fold, and
/// snapshots all agree (Architecture §4/§6a).
/// </summary>
public static class SyncColumns
{
    public const string Id = "id";
    public const string RowVersion = "row_version";
    public const string BaseVersion = "base_version";
    public const string ModifiedAt = "modified_at";
    public const string ModifiedBy = "modified_by";
    public const string IsDeleted = "is_deleted";
}
