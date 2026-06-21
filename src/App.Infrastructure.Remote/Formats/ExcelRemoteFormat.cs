using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;
using ClosedXML.Excel;

namespace App.Infrastructure.Remote.Formats;

/// <summary>
/// Excel (.xlsx) format via ClosedXML — convenient for human inspection. Heaviest format; not
/// recommended for large snapshots or logs (Architecture §6a). Values are written typed and read back
/// to canonical strings.
/// </summary>
public sealed class ExcelRemoteFormat : IRemoteFormat
{
    public string Name => "excel";

    public string Extension => ".xlsx";

    public void Write(string path, RemoteTable table)
    {
        using XLWorkbook workbook = new();
        IXLWorksheet sheet = workbook.AddWorksheet("data");

        for (int col = 0; col < table.Columns.Count; col++)
        {
            sheet.Cell(1, col + 1).Value = table.Columns[col].Name;
        }

        for (int r = 0; r < table.Rows.Count; r++)
        {
            IReadOnlyList<string?> row = table.Rows[r];
            for (int col = 0; col < table.Columns.Count; col++)
            {
                IXLCell cell = sheet.Cell(r + 2, col + 1);
                object? native = RemoteValueConverter.ToNative(table.Columns[col].Type, col < row.Count ? row[col] : null);
                cell.Value = native switch
                {
                    null => Blank.Value,
                    long l => l,
                    double d => d,
                    bool b => b,
                    _ => XLCellValue.FromObject(native.ToString()),
                };
            }
        }

        workbook.SaveAs(path);
    }

    public RemoteTable Read(string path)
    {
        using XLWorkbook workbook = new(path);
        IXLWorksheet sheet = workbook.Worksheet(1);
        IXLRange used = sheet.RangeUsed() ?? sheet.Range(1, 1, 1, 1);

        int firstRow = used.FirstRow().RowNumber();
        int firstCol = used.FirstColumn().ColumnNumber();
        int lastRow = used.LastRow().RowNumber();
        int lastCol = used.LastColumn().ColumnNumber();

        List<RemoteColumn> columns = [];
        for (int col = firstCol; col <= lastCol; col++)
        {
            columns.Add(new RemoteColumn(sheet.Cell(firstRow, col).GetString(), CatalogValueType.Text));
        }

        List<IReadOnlyList<string?>> rows = [];
        for (int r = firstRow + 1; r <= lastRow; r++)
        {
            List<string?> row = [];
            for (int col = firstCol; col <= lastCol; col++)
            {
                row.Add(Canonicalize(sheet.Cell(r, col)));
            }

            rows.Add(row);
        }

        return new RemoteTable(columns, rows);
    }

    /// <summary>Reads a cell back to a canonical string by its stored type (so booleans/numbers match what was written).</summary>
    private static string? Canonicalize(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        XLCellValue value = cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.Number => RemoteValueConverter.ToCanonical(CatalogValueType.Number, value.GetNumber()),
            XLDataType.DateTime => value.GetDateTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            _ => cell.GetString(),
        };
    }
}
