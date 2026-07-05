namespace App.Application.Importing;

/// <summary>
/// Reads a spreadsheet workbook (e.g. .xlsx) into the importer's <see cref="WorksheetData"/> shape:
/// for each worksheet named in the mapping, the first used row is the header and every subsequent used
/// row is a header → cell-text record. The concrete reader (ClosedXML) lives in the infrastructure
/// layer so the Application engine and the UI stay free of the Excel dependency.
/// </summary>
public interface IWorkbookReader
{
    /// <summary>Reads the worksheets named in <paramref name="mapping"/> from the workbook stream.</summary>
    IReadOnlyList<WorksheetData> Read(Stream workbook, ImportMapping mapping);

    /// <summary>
    /// Reads EVERY worksheet in the workbook — the wizard's mapping editor needs the sheets no mapping
    /// covers yet, so the user can map them to tables.
    /// </summary>
    IReadOnlyList<WorksheetData> ReadAll(Stream workbook);
}
