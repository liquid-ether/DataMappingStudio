using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Importer;

/// <summary>An FK column resolved by natural key: the target table and the column whose value to match.</summary>
public sealed record ColumnReference(
    [property: JsonPropertyName("table")] string Table,
    [property: JsonPropertyName("by")] string By);

/// <summary>
/// One worksheet→table mapping (Architecture §10b). <see cref="Columns"/> maps source headers to
/// catalog columns — anything not listed (derived/computed columns) is intentionally not imported.
/// </summary>
public sealed record WorksheetMapping
{
    [JsonPropertyName("worksheet")] public required string Worksheet { get; init; }

    [JsonPropertyName("table")] public required string Table { get; init; }

    [JsonPropertyName("naturalKey")] public IReadOnlyList<string> NaturalKey { get; init; } = [];

    [JsonPropertyName("columns")] public IReadOnlyDictionary<string, string> Columns { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("references")] public IReadOnlyDictionary<string, ColumnReference> References { get; init; } = new Dictionary<string, ColumnReference>();

    /// <summary>The catalog column whose value is an expression to resolve (e.g. <c>expression</c>), if any.</summary>
    [JsonPropertyName("expressionColumn")] public string? ExpressionColumn { get; init; }
}

/// <summary>The editable worksheet→table mapping config that drives the importer.</summary>
public sealed record ImportMapping
{
    [JsonPropertyName("worksheets")] public IReadOnlyList<WorksheetMapping> Worksheets { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static ImportMapping FromJson(string json)
        => JsonSerializer.Deserialize<ImportMapping>(json, Options) ?? throw new FormatException("Import mapping JSON deserialized to null.");
}
