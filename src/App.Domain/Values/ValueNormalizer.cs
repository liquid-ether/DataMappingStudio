using System.Globalization;
using App.Domain.Catalog;

namespace App.Domain.Values;

/// <summary>
/// Normalizes raw string values to a canonical, culture-invariant representation per
/// <see cref="CatalogValueType"/>. So imported values compare cleanly against later edits and the
/// field-level merge engine never flags a spurious conflict caused by formatting differences
/// (Architecture §10a). Returns <c>null</c> for empty input (empty == "no value").
/// </summary>
public static class ValueNormalizer
{
    private static readonly string[] TrueTokens = ["1", "true", "yes", "y", "vrai", "oui", "o"];
    private static readonly string[] FalseTokens = ["0", "false", "no", "n", "faux", "non"];

    /// <summary>Normalizes <paramref name="raw"/> for the given type, throwing on malformed input.</summary>
    public static string? Normalize(CatalogValueType type, string? raw)
    {
        if (!TryNormalize(type, raw, out string? normalized, out string? error))
        {
            throw new FormatException(error);
        }

        return normalized;
    }

    /// <summary>Attempts to normalize <paramref name="raw"/> for the given type.</summary>
    public static bool TryNormalize(CatalogValueType type, string? raw, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;

        if (raw is null)
        {
            return true;
        }

        string trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        switch (type)
        {
            case CatalogValueType.Text:
                normalized = trimmed;
                return true;

            case CatalogValueType.Integer:
                if (long.TryParse(trimmed, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l))
                {
                    normalized = l.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                error = $"'{raw}' is not a valid integer.";
                return false;

            case CatalogValueType.Number:
                // Forbid group separators so the comma is unambiguously a French decimal point and the
                // canonical form is stable (invariant decimal point first, then French comma).
                const NumberStyles numberStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
                if (decimal.TryParse(trimmed, numberStyles, CultureInfo.InvariantCulture, out decimal d)
                    || decimal.TryParse(trimmed, numberStyles, CultureInfo.GetCultureInfo("fr-FR"), out d))
                {
                    // Drop trailing zeros so "5.00" and "5" compare equal ('0.####…' omits them).
                    normalized = d.ToString("0.############################", CultureInfo.InvariantCulture);
                    return true;
                }

                error = $"'{raw}' is not a valid number.";
                return false;

            case CatalogValueType.Boolean:
                string lower = trimmed.ToLowerInvariant();
                if (Array.IndexOf(TrueTokens, lower) >= 0)
                {
                    normalized = "true";
                    return true;
                }

                if (Array.IndexOf(FalseTokens, lower) >= 0)
                {
                    normalized = "false";
                    return true;
                }

                error = $"'{raw}' is not a valid boolean.";
                return false;

            case CatalogValueType.Date:
                if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset date)
                    || DateTimeOffset.TryParse(trimmed, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date))
                {
                    normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return true;
                }

                error = $"'{raw}' is not a valid date.";
                return false;

            case CatalogValueType.Timestamp:
                if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset ts)
                    || DateTimeOffset.TryParse(trimmed, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out ts))
                {
                    normalized = ts.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                    return true;
                }

                error = $"'{raw}' is not a valid timestamp.";
                return false;

            case CatalogValueType.Uuid:
                if (Guid.TryParse(trimmed, out Guid g))
                {
                    normalized = g.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
                    return true;
                }

                error = $"'{raw}' is not a valid uuid.";
                return false;

            default:
                error = $"Unsupported value type '{type}'.";
                return false;
        }
    }
}
