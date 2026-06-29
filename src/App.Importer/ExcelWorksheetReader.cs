using ClosedXML.Excel;

namespace App.Importer;

/// <summary>
/// Reads an Excel workbook (.xlsx) directly into the importer's <see cref="WorksheetData"/> shape —
/// no external tooling (the build's PowerShell <c>ImportExcel</c> step is now optional). For each
/// worksheet named in the mapping it takes the first used row as the header and every subsequent used
/// row as a record of header → cell text (Excel's displayed value, so dates/numbers come through as the
/// user sees them). Worksheets that are absent or empty yield an empty <see cref="WorksheetData"/>.
/// </summary>
public static class ExcelWorksheetReader
{
    public static List<WorksheetData> Read(string workbookPath, ImportMapping mapping)
    {
        using XLWorkbook workbook = new(workbookPath);
        return Read(workbook, mapping);
    }

    public static List<WorksheetData> Read(Stream workbookStream, ImportMapping mapping)
    {
        using XLWorkbook workbook = new(workbookStream);
        return Read(workbook, mapping);
    }

    private static List<WorksheetData> Read(XLWorkbook workbook, ImportMapping mapping)
    {
        List<WorksheetData> result = [];
        foreach (WorksheetMapping ws in mapping.Worksheets)
        {
            result.Add(workbook.Worksheets.TryGetWorksheet(ws.Worksheet, out IXLWorksheet? sheet)
                ? new WorksheetData(ws.Worksheet, ReadRows(sheet))
                : new WorksheetData(ws.Worksheet, []));
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ReadRows(IXLWorksheet sheet)
    {
        List<IXLRangeRow> used = sheet.RangeUsed()?.RowsUsed().ToList() ?? [];
        if (used.Count == 0)
        {
            return [];
        }

        // First used row = headers (column number -> trimmed header text); blank headers are dropped.
        List<(int Column, string Name)> headers = used[0].CellsUsed()
            .Select(c => (c.Address.ColumnNumber, Name: c.GetFormattedString().Trim()))
            .Where(h => h.Name.Length > 0)
            .ToList();

        List<IReadOnlyDictionary<string, string?>> rows = [];
        for (int i = 1; i < used.Count; i++)
        {
            IXLRangeRow row = used[i];
            Dictionary<string, string?> record = new(StringComparer.Ordinal);
            foreach ((int column, string name) in headers)
            {
                IXLCell cell = row.Cell(column);
                string text = cell.GetFormattedString();
                record[name] = string.IsNullOrEmpty(text) ? null : text;
            }

            rows.Add(record);
        }

        return rows;
    }
}
