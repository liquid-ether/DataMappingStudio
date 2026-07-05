using App.Domain.Catalog;

namespace App.Application.Abstractions;

/// <summary>The shared runtime-settings document (whole-file last-writer-wins; admin writes are rare).</summary>
public sealed record SettingsDocument(
    IReadOnlyDictionary<string, string> Values,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy);

/// <summary>Persistence for shared-scope runtime settings (a document in the synced folder).</summary>
public interface ISharedSettingsStore
{
    /// <summary>Cheap change signature (size+mtime) so readers skip unchanged documents.</summary>
    string Version();

    SettingsDocument? Read();

    void Write(SettingsDocument document);
}

/// <summary>
/// Runtime-changeable operational settings (finally wiring the reserved <see cref="AppConfigEntry"/> /
/// <see cref="ConfigScope"/> model): Shared-scope values live in the synced folder so every host
/// converges; Local-scope values live in a per-host file. Consumers read through this per use (values
/// re-read when the document's signature changes), so live-apply settings need no restart.
/// </summary>
public interface IRuntimeConfig
{
    /// <summary>The registered setting's current value (stored, else its registry default).</summary>
    string Get(string key);

    int GetInt(string key, int fallback);

    /// <summary>Writes a registered setting (validates key + value; permission-checked by the caller's user).</summary>
    void Set(AppConfigEntry entry, ICurrentUser user);

    event Action? Changed;
}
