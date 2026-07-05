using System.Text.Json;
using App.Application.Abstractions;
using App.Application.Security;
using App.Domain.Catalog;

namespace App.Application.Configuration;

/// <summary>
/// Read-through runtime settings: Shared-scope values come from the synced folder's settings document
/// (re-read when its signature changes — no polling machinery), Local-scope values from a per-host JSON
/// file. Registered as a host singleton; safe without a shared store (offline/tests: defaults + local).
/// </summary>
public sealed class RuntimeConfigService(ISharedSettingsStore? shared = null, string? localFilePath = null) : IRuntimeConfig
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object _gate = new();
    private string? _sharedVersion;
    private Dictionary<string, string> _shared = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _local;

    public event Action? Changed;

    public string Get(string key)
    {
        SettingDefinition definition = SettingsRegistry.Find(key)
            ?? throw new ArgumentException($"Unknown setting '{key}'.", nameof(key));

        Dictionary<string, string> values = definition.Scope == ConfigScope.Shared ? SharedValues() : LocalValues();
        return values.TryGetValue(definition.Key, out string? value) ? value : definition.DefaultValue;
    }

    public int GetInt(string key, int fallback)
        => int.TryParse(Get(key), out int value) ? value : fallback;

    public void Set(AppConfigEntry entry, ICurrentUser user)
    {
        if (!user.HasPermission(Permissions.AppConfigure))
        {
            throw new UnauthorizedAccessException("Changing settings requires the App.Configure permission.");
        }

        SettingDefinition definition = SettingsRegistry.Find(entry.Key)
            ?? throw new ArgumentException($"Unknown setting '{entry.Key}'.");
        IReadOnlyList<string> errors = SettingsRegistry.Validate(definition, entry.Value ?? "");
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        if (definition.Scope == ConfigScope.Shared)
        {
            if (shared is null)
            {
                throw new InvalidOperationException("No shared folder is configured — shared settings are unavailable.");
            }

            lock (_gate)
            {
                Dictionary<string, string> values = new(SharedValues(), StringComparer.OrdinalIgnoreCase)
                {
                    [definition.Key] = entry.Value ?? definition.DefaultValue,
                };
                shared.Write(new SettingsDocument(values, DateTimeOffset.UtcNow, user.Name));
                _sharedVersion = null; // force a re-read next access
            }
        }
        else
        {
            lock (_gate)
            {
                Dictionary<string, string> values = LocalValues();
                values[definition.Key] = entry.Value ?? definition.DefaultValue;
                _local = values;
                if (localFilePath is not null)
                {
                    File.WriteAllText(localFilePath, JsonSerializer.Serialize(values, Json));
                }
            }
        }

        Changed?.Invoke();
    }

    private Dictionary<string, string> SharedValues()
    {
        if (shared is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        string version = SafeVersion();
        lock (_gate)
        {
            if (version == _sharedVersion)
            {
                return _shared;
            }
        }

        Dictionary<string, string> values;
        try
        {
            values = shared.Read()?.Values is { } read
                ? new Dictionary<string, string>(read, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return _shared; // shared folder briefly unavailable / mid-sync — keep the last good values
        }

        lock (_gate)
        {
            _sharedVersion = version;
            _shared = values;
        }

        return values;
    }

    private string SafeVersion()
    {
        try
        {
            return shared!.Version();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _sharedVersion ?? "0";
        }
    }

    private Dictionary<string, string> LocalValues()
    {
        lock (_gate)
        {
            if (_local is not null)
            {
                return _local;
            }

            try
            {
                _local = localFilePath is not null && File.Exists(localFilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(localFilePath)) ?? []
                    : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _local = [];
            }

            _local = new Dictionary<string, string>(_local, StringComparer.OrdinalIgnoreCase);
            return _local;
        }
    }
}
