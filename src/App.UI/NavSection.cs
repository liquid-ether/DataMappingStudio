namespace App.UI;

/// <summary>One group of side-navigation destinations (a bilingual section header + its items).</summary>
public sealed record NavSection(string TitleEn, string TitleFr, IReadOnlyList<(string Href, string Key)> Items);
