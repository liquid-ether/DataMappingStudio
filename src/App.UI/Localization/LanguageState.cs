using System.Globalization;

namespace App.UI.Localization;

/// <summary>
/// Holds the current UI language and raises a change event so components re-render instantly on
/// toggle (matching the mockup's EN/FR switch). UI chrome strings are looked up here; catalog data
/// labels are chosen with <see cref="Label"/> (label_en/label_fr). Scoped per Blazor circuit.
/// </summary>
public sealed class LanguageState
{
    private static readonly Dictionary<string, (string En, string Fr)> Strings = new(StringComparer.Ordinal)
    {
        ["brand"] = ("Mapping Studio", "Studio de mappage"),
        ["publish"] = ("Publish", "Publier"),
        ["tabMappings"] = ("Mappings", "Mappages"),
        ["tabLineage"] = ("Lineage", "Traçabilité"),
        ["navApplications"] = ("Applications", "Applications"),
        ["navSources"] = ("Sources", "Sources"),
        ["navDictionary"] = ("Dictionary", "Dictionnaire"),
        ["navConfig"] = ("Config", "Config"),
        ["navClassification"] = ("Classification", "Classification"),
        ["navRules"] = ("Rules", "Règles"),
        ["navDemo"] = ("Catalog demo", "Démo catalogue"),
        ["navHistory"] = ("History", "Historique"),
        ["home"] = ("Home", "Accueil"),
        ["welcome"] = ("Metadata-driven data mapping & lineage.", "Mappage et traçabilité pilotés par métadonnées."),
        ["addRow"] = ("Add row", "Ajouter une ligne"),
        ["addColumn"] = ("Add column", "Ajouter une colonne"),
        ["save"] = ("Save", "Enregistrer"),
        ["cancel"] = ("Cancel", "Annuler"),
        ["search"] = ("Search", "Rechercher"),
        ["noRows"] = ("No rows yet.", "Aucune ligne."),
        ["requiredMissing"] = ("Fill all required fields.", "Renseignez les champs obligatoires."),
        ["columnName"] = ("Column name", "Nom de colonne"),
    };

    public string Current { get; private set; } = "en";

    public bool IsFrench => Current == "fr";

    public event Action? OnChange;

    public void Set(string language)
    {
        if (language == Current)
        {
            return;
        }

        Current = language;
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        OnChange?.Invoke();
    }

    /// <summary>A UI chrome string by key (falls back to the key itself).</summary>
    public string T(string key)
        => Strings.TryGetValue(key, out (string En, string Fr) s) ? (IsFrench ? s.Fr : s.En) : key;

    /// <summary>Pick a bilingual catalog label for the current language.</summary>
    public string Label(string? en, string? fr)
        => IsFrench ? (fr ?? en ?? string.Empty) : (en ?? fr ?? string.Empty);
}
