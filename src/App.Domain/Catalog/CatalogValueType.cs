namespace App.Domain.Catalog;

/// <summary>
/// Canonical value types (Architecture §4 normalization pass: "uuid · text · int · bool · timestamp"
/// with max-length a catalog attribute). Named <c>CatalogValueType</c> to avoid colliding with
/// <see cref="System.ValueType"/>. The UI maps these to the mockup's type badges (text/number/date/bool).
/// </summary>
public enum CatalogValueType
{
    Text = 0,
    Integer = 1,
    Number = 2,
    Boolean = 3,
    Date = 4,
    Timestamp = 5,
    Uuid = 6,
}
