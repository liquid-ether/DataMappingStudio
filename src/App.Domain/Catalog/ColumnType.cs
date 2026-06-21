using System.Globalization;
using System.Text.RegularExpressions;

namespace App.Domain.Catalog;

/// <summary>
/// A resolved column value type: the canonical <see cref="CatalogValueType"/> plus an optional
/// maximum length (for text). Produced from the workbook's SQL-style annotations
/// (e.g. <c>VARCHAR(50)</c>, <c>VARCHAR(MAX)</c>, <c>UUID</c>, <c>INT</c>) by <see cref="Parse"/>.
/// </summary>
public readonly partial record struct ColumnType(CatalogValueType ValueType, int? MaxLength = null)
{
    /// <summary>Parses a workbook SQL-style type annotation into a canonical <see cref="ColumnType"/>.</summary>
    /// <exception cref="FormatException">The annotation is empty or its base type is unrecognized.</exception>
    public static ColumnType Parse(string annotation)
    {
        if (!TryParse(annotation, out ColumnType result, out string? error))
        {
            throw new FormatException(error);
        }

        return result;
    }

    /// <summary>Attempts to parse a workbook SQL-style type annotation.</summary>
    public static bool TryParse(string? annotation, out ColumnType result, out string? error)
    {
        result = default;
        error = null;

        if (string.IsNullOrWhiteSpace(annotation))
        {
            error = "Type annotation is empty.";
            return false;
        }

        string text = annotation.Trim();
        Match varchar = VarcharPattern().Match(text);
        if (varchar.Success)
        {
            string size = varchar.Groups["size"].Value;
            int? max = size.Equals("MAX", StringComparison.OrdinalIgnoreCase)
                ? null
                : int.Parse(size, CultureInfo.InvariantCulture);
            result = new ColumnType(CatalogValueType.Text, max);
            return true;
        }

        string baseType = text.ToUpperInvariant();
        CatalogValueType? value = baseType switch
        {
            "UUID" or "UNIQUEID" or "UNIQUEIDENTIFIER" or "GUID" => CatalogValueType.Uuid,
            "TEXT" or "STRING" or "NVARCHAR" or "VARCHAR" or "CHAR" or "NCHAR" => CatalogValueType.Text,
            "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT" => CatalogValueType.Integer,
            "DECIMAL" or "NUMERIC" or "NUMBER" or "FLOAT" or "REAL" or "DOUBLE" or "MONEY" => CatalogValueType.Number,
            "BOOL" or "BOOLEAN" or "BIT" => CatalogValueType.Boolean,
            "DATE" => CatalogValueType.Date,
            "TIMESTAMP" or "DATETIME" or "DATETIME2" or "DATETIMEOFFSET" or "SMALLDATETIME" => CatalogValueType.Timestamp,
            _ => null,
        };

        if (value is null)
        {
            error = $"Unrecognized type annotation '{annotation}'.";
            return false;
        }

        result = new ColumnType(value.Value);
        return true;
    }

    [GeneratedRegex(@"^N?(?:VAR)?CHAR\s*\(\s*(?<size>MAX|\d+)\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VarcharPattern();
}
