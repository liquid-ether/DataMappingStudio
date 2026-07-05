using App.Domain.Catalog;

namespace App.Application.Configuration;

/// <summary>The value shape a setting accepts (drives the editor + validation).</summary>
public enum SettingType
{
    Integer = 0,
    Boolean = 1,
    Text = 2,
    Language = 3, // "en" | "fr"
}

/// <summary>One runtime-changeable setting the app knows about.</summary>
public sealed record SettingDefinition(
    string Key,
    ConfigScope Scope,
    SettingType Type,
    string DefaultValue,
    bool LiveApply,
    string DescriptionEn,
    string DescriptionFr,
    int Min = 0,
    int Max = int.MaxValue);

/// <summary>
/// The closed list of runtime-changeable settings — the admin Settings page renders from this, so
/// unknown keys can't be invented, and every consumer reads through <c>IRuntimeConfig</c> with the
/// registry default as fallback. Secrets and structural configuration (paths, auth, proxy) deliberately
/// stay file-based and are shown read-only.
/// </summary>
public static class SettingsRegistry
{
    public const string AutoRefreshSeconds = "Sync.AutoRefreshSeconds";
    public const string WorkspaceIdleMinutes = "Workspaces.MaxIdleMinutes";
    public const string WorkspaceRetentionDays = "Workspaces.RetentionDays";
    public const string DefaultLanguage = "Ui.DefaultLanguage";

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(AutoRefreshSeconds, ConfigScope.Shared, SettingType.Integer, "30", LiveApply: true,
            "How often each workspace fast-forwards from the shared folder, in seconds.",
            "Fréquence à laquelle chaque espace de travail se synchronise du dossier partagé, en secondes.",
            Min: 5, Max: 3600),
        new(WorkspaceIdleMinutes, ConfigScope.Shared, SettingType.Integer, "30", LiveApply: true,
            "Minutes of inactivity before an idle workspace is closed (its files stay cached).",
            "Minutes d'inactivité avant la fermeture d'un espace de travail inactif (ses fichiers restent en cache).",
            Min: 5, Max: 24 * 60),
        new(WorkspaceRetentionDays, ConfigScope.Shared, SettingType.Integer, "90", LiveApply: true,
            "Days before the working copy of a user not seen is deleted (published work is safe in the shared folder).",
            "Jours avant la suppression de la copie de travail d'un utilisateur absent (le travail publié reste dans le dossier partagé).",
            Min: 1, Max: 3650),
        new(DefaultLanguage, ConfigScope.Shared, SettingType.Language, "en", LiveApply: true,
            "Language new sessions start in (users can still toggle EN/FR).",
            "Langue de démarrage des nouvelles sessions (le bouton EN/FR reste disponible)."),
    ];

    public static SettingDefinition? Find(string key)
        => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Validates a value against its definition; empty = valid.</summary>
    public static IReadOnlyList<string> Validate(SettingDefinition definition, string value)
    {
        List<string> errors = [];
        switch (definition.Type)
        {
            case SettingType.Integer when !int.TryParse(value, out int n) || n < definition.Min || n > definition.Max:
                errors.Add($"'{definition.Key}' must be an integer between {definition.Min} and {definition.Max}.");
                break;
            case SettingType.Boolean when value is not ("true" or "false"):
                errors.Add($"'{definition.Key}' must be true or false.");
                break;
            case SettingType.Language when value is not ("en" or "fr"):
                errors.Add($"'{definition.Key}' must be en or fr.");
                break;
        }

        return errors;
    }
}
