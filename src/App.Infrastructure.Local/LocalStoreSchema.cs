namespace App.Infrastructure.Local;

/// <summary>
/// Fixed core columns the generic store manages on every table (IDs + sync metadata). Data columns
/// described by the catalog are stored as TEXT holding canonical values, so the Row⇄DB mapping and
/// change-log comparisons stay exact and free of affinity-coercion surprises (Architecture §4).
/// </summary>
internal static class LocalStoreSchema
{
    public const string Id = "id";
    public const string RowVersion = "row_version";
    public const string BaseVersion = "base_version";
    public const string ModifiedAt = "modified_at";
    public const string ModifiedBy = "modified_by";
    public const string IsDeleted = "is_deleted";

    public static readonly IReadOnlySet<string> CoreColumns =
        new HashSet<string>(StringComparer.Ordinal) { Id, RowVersion, BaseVersion, ModifiedAt, ModifiedBy, IsDeleted };

    /// <summary>The core-column DDL fragment shared by every domain table.</summary>
    public const string CoreColumnsDdl =
        $"\"{Id}\" TEXT PRIMARY KEY, " +
        $"\"{RowVersion}\" INTEGER NOT NULL DEFAULT 0, " +
        $"\"{BaseVersion}\" INTEGER NOT NULL DEFAULT 0, " +
        $"\"{ModifiedAt}\" TEXT, " +
        $"\"{ModifiedBy}\" TEXT, " +
        $"\"{IsDeleted}\" INTEGER NOT NULL DEFAULT 0";
}
