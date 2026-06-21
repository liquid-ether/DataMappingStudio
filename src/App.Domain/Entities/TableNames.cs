namespace App.Domain.Entities;

/// <summary>
/// Canonical (normalized, snake_case) physical table names. Central so the generic store, catalog
/// seeding, importer mapping and the engines all agree (Architecture §4 name map).
/// </summary>
public static class TableNames
{
    public const string Application = "application";
    public const string DataSource = "data_source";
    public const string DictionaryEntry = "dictionary_entry";
    public const string Rule = "rule";
    public const string Mapping = "mapping";
    public const string LookupValue = "lookup_value";
    public const string Classification = "classification";

    // Meta tables (generic, folded like any other table).
    public const string ColumnCatalog = "column_catalog";
    public const string AuditLog = "audit_log";
    public const string AppConfig = "app_config";

    /// <summary>The domain tables (excludes the meta tables), in FK dependency order.</summary>
    public static IReadOnlyList<string> DomainTablesInDependencyOrder { get; } =
    [
        Application,
        DataSource,
        LookupValue,
        Classification,
        DictionaryEntry,
        Rule,
        Mapping,
    ];
}
