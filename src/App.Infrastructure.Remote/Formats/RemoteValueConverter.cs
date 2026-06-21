using System.Globalization;
using App.Domain.Catalog;
using App.Domain.Values;

namespace App.Infrastructure.Remote.Formats;

/// <summary>
/// Converts between canonical string values (as used everywhere in the app) and the native CLR types
/// the typed formats (Parquet/Excel) store, so types travel natively while the rest of the pipeline
/// stays string-based.
/// </summary>
internal static class RemoteValueConverter
{
    /// <summary>The CLR type a column of <paramref name="type"/> is stored as in typed formats.</summary>
    public static Type NativeType(CatalogValueType type) => type switch
    {
        CatalogValueType.Integer => typeof(long?),
        CatalogValueType.Number => typeof(double?),
        CatalogValueType.Boolean => typeof(bool?),
        _ => typeof(string),
    };

    public static object? ToNative(CatalogValueType type, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical))
        {
            return null;
        }

        return type switch
        {
            CatalogValueType.Integer => long.Parse(canonical, CultureInfo.InvariantCulture),
            CatalogValueType.Number => double.Parse(canonical, CultureInfo.InvariantCulture),
            CatalogValueType.Boolean => canonical == "true",
            _ => canonical,
        };
    }

    public static string? ToCanonical(CatalogValueType type, object? native)
    {
        if (native is null)
        {
            return null;
        }

        return type switch
        {
            CatalogValueType.Integer => Convert.ToInt64(native, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            CatalogValueType.Number => ValueNormalizer.Normalize(CatalogValueType.Number, Convert.ToDouble(native, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)),
            CatalogValueType.Boolean => Convert.ToBoolean(native, CultureInfo.InvariantCulture) ? "true" : "false",
            _ => native.ToString(),
        };
    }
}
