namespace App.Domain.Entities;

/// <summary>
/// A data source belonging to an application (workbook "Source", table <c>data_source</c>). The
/// <c>field_count</c> ("NB Fields") is a computed column and is therefore not stored here.
/// Type/bloc/frequency/data-product are reference columns into <c>lookup_value</c>.
/// </summary>
public sealed record DataSource
{
    public Guid Id { get; init; }

    public Guid ApplicationId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? FullName { get; init; }

    public string? Extraction { get; init; }

    public string? Characteristics { get; init; }

    public string? ExchangeContract { get; init; }

    public Guid? TypeLookupId { get; init; }

    public Guid? BlocLookupId { get; init; }

    public Guid? FrequencyLookupId { get; init; }

    public Guid? DataProductLookupId { get; init; }

    public string? IngestionPath { get; init; }

    public string? BronzePath { get; init; }

    public string? SilverPath { get; init; }

    public string? GoldPath { get; init; }

    public SyncMetadata Sync { get; init; } = SyncMetadata.New();

    public GovernanceMetadata Governance { get; init; }
}
