namespace App.Domain.Catalog;

/// <summary>Scope of a runtime configuration setting (Diagrams: <c>app_config.scope</c>).</summary>
public enum ConfigScope
{
    /// <summary>Machine-local setting; never synced (e.g. canonical folder path).</summary>
    Local = 0,

    /// <summary>Shared setting that participates in the fold (e.g. remote format, refresh interval).</summary>
    Shared = 1,
}

/// <summary>
/// A runtime configuration setting. Splitting the workbook's overloaded <c>Config</c> sheet, the
/// typed bilingual enumerations live in <c>lookup_value</c> while these runtime settings live in
/// <c>app_config</c> (Architecture §4 normalization, Q-N1).
/// </summary>
public sealed record AppConfigEntry
{
    public required string Key { get; init; }

    public string? Value { get; init; }

    public ConfigScope Scope { get; init; } = ConfigScope.Local;
}
