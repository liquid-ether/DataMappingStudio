namespace App.Domain.Entities;

/// <summary>
/// A typed, bilingual enumeration value (workbook "Config", table <c>lookup_value</c>).
/// <see cref="LookupType"/> partitions it (SourceType, Status, Frequency, BlocDefinition,
/// DataProduct, Langue, Tokenisation, …); most reference columns point here.
/// </summary>
public sealed record LookupValue
{
    public Guid Id { get; init; }

    public required string LookupType { get; init; }

    public required string Value { get; init; }

    public string? DescriptionFr { get; init; }

    public string? DescriptionEn { get; init; }

    public SyncMetadata Sync { get; init; } = SyncMetadata.New();
}
