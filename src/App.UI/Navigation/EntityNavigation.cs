using App.Application.Abstractions;
using App.Application.Provisioning;
using App.Domain.Catalog;
using App.UI.Localization;

namespace App.UI.Navigation;

/// <summary>
/// Builds the Data-model navigation items: the built-in entities (i18n keys, fixed order) followed by
/// runtime-created tables that opted into navigation (<see cref="TableCatalogEntry.NavVisible"/>,
/// ordered by <see cref="TableCatalogEntry.NavOrder"/>, literal bilingual labels). Static + null-tolerant
/// so shells/tests without a table catalog degrade to the defaults.
/// </summary>
public static class EntityNavigation
{
    public static IReadOnlyList<NavItem> EntityItems(ITableCatalog? tables)
    {
        List<NavItem> items = [.. DefaultCatalog.Navigation.Select(n => new NavItem($"t/{n.Table}", n.NavKey))];
        if (tables is null)
        {
            return items;
        }

        HashSet<string> defaults = DefaultCatalog.Navigation.Select(n => n.Table).ToHashSet(StringComparer.Ordinal);
        items.AddRange(tables.GetTableMeta()
            .Where(t => t.NavVisible && !defaults.Contains(t.TableName))
            .OrderBy(t => t.NavOrder).ThenBy(t => t.TableName, StringComparer.Ordinal)
            .Select(t => new NavItem($"t/{t.TableName}", null, t.LabelEn ?? t.TableName, t.LabelFr ?? t.LabelEn ?? t.TableName)));
        return items;
    }

    /// <summary>The localized page title for an entity table (default nav key, table meta, or the raw name).</summary>
    public static string TitleFor(string table, ITableCatalog? tables, LanguageState lang)
    {
        EntityNav? nav = DefaultCatalog.Navigation.FirstOrDefault(n => n.Table == table);
        if (nav is not null)
        {
            return lang.T(nav.NavKey);
        }

        TableCatalogEntry? meta = tables?.GetTableMeta(table);
        return meta is null
            ? table
            : (lang.IsFrench ? meta.LabelFr ?? meta.LabelEn : meta.LabelEn ?? meta.LabelFr) ?? table;
    }

    /// <summary>Resolves a <see cref="NavItem"/>'s display label.</summary>
    public static string Label(NavItem item, LanguageState lang)
        => item.NavKey is not null
            ? lang.T(item.NavKey)
            : (lang.IsFrench ? item.LabelFr ?? item.LabelEn : item.LabelEn ?? item.LabelFr) ?? item.Href;
}
