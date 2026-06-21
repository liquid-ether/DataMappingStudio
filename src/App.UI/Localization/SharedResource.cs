namespace App.UI.Localization;

/// <summary>
/// Marker type for the shared EN/FR string resources. Components resolve
/// <c>IStringLocalizer&lt;SharedResource&gt;</c> to look up UI strings (keys mirror the mockup's
/// i18n table). Bilingual <em>data</em> labels still come from the column catalog (label_en/label_fr).
/// </summary>
public sealed class SharedResource
{
}
