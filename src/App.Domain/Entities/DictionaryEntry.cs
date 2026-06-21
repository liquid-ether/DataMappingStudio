namespace App.Domain.Entities;

/// <summary>
/// A column definition within a data source (workbook "Dictionnary", table <c>dictionary_entry</c>).
/// <c>unique_key</c> ("Source.Colonne") is computed and not stored. The privacy block points at
/// <c>classification</c>; access/disclosure values are autofilled from it at display time, never stored.
/// </summary>
public sealed record DictionaryEntry
{
    public Guid Id { get; init; }

    public Guid SourceId { get; init; }

    public required string ColumnName { get; init; }

    public int Ordinal { get; init; }

    /// <summary>The column's declared data type as authored ("Type de donnees", e.g. <c>VARCHAR(100)</c>).</summary>
    public string? DataType { get; init; }

    public bool IsSurrogateKey { get; init; }

    public bool IsPrimaryKey { get; init; }

    public bool IsNullable { get; init; } = true;

    public string? BusinessName { get; init; }

    public string? Description { get; init; }

    public string? FsdfField { get; init; }

    public Guid? LanguageLookupId { get; init; }

    public Guid? ClassificationId { get; init; }

    /// <summary>Tokenisation/security type, a reference into <c>lookup_value</c> (Type=Tokenisation).</summary>
    public Guid? SecurityLookupId { get; init; }

    public string? DataMask { get; init; }

    public string? DataExamples { get; init; }

    public SyncMetadata Sync { get; init; } = SyncMetadata.New();

    public GovernanceMetadata Governance { get; init; }
}
