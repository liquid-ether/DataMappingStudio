using App.Application.Importing;
using ClosedXML.Excel;

namespace App.Infrastructure.Local;

/// <summary>
/// <see cref="IWorkbookReader"/> backed by ClosedXML: reads an .xlsx workbook directly (no external
/// tooling). For each worksheet named in the mapping it takes the first used row as the header and every
/// subsequent used row as a record of header → cell text (Excel's displayed value, so dates/numbers come
/// through as the user sees them). Worksheets that are absent or empty yield an empty
/// <see cref="WorksheetData"/>.
/// </summary>
public sealed class ClosedXmlWorkbookReader : IWorkbookReader
{
    public IReadOnlyList<WorksheetData> Read(Stream workbook, ImportMapping mapping)
    {
        using XLWorkbook xl = new(workbook);
        List<WorksheetData> result = [];
        foreach (WorksheetMapping ws in mapping.Worksheets)
        {
            result.Add(xl.Worksheets.TryGetWorksheet(ws.Worksheet, out IXLWorksheet? sheet)
                ? new WorksheetData(ws.Worksheet, ReadRows(sheet))
                : new WorksheetData(ws.Worksheet, []));
        }

        return result;
    }

    public IReadOnlyList<WorksheetData> ReadAll(Stream workbook)
    {
        using XLWorkbook xl = new(workbook);
        return xl.Worksheets.Select(sheet => new WorksheetData(sheet.Name, ReadRows(sheet))).ToList();
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
                string text = row.Cell(column).GetFormattedString();
                record[name] = string.IsNullOrEmpty(text) ? null : text;
            }

            rows.Add(record);
        }

        return rows;
    }
}
