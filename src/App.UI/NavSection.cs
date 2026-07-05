namespace App.UI;

/// <summary>
/// One side-navigation destination. Default entities carry an i18n <paramref name="NavKey"/> (resolved
/// through <c>LanguageState</c>); runtime-created tables carry literal bilingual labels instead.
/// </summary>
public sealed record NavItem(string Href, string? NavKey = null, string? LabelEn = null, string? LabelFr = null);

/// <summary>One group of side-navigation destinations (a bilingual section header + its items).</summary>
public sealed record NavSection(string TitleEn, string TitleFr, IReadOnlyList<NavItem> Items);
