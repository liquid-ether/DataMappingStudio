using System.Text.Json;

namespace App.Application.Importing;

/// <summary>Per-worksheet import outcome (Architecture §10d).</summary>
public sealed class WorksheetReport(string worksheet, string table)
{
    public string Worksheet { get; } = worksheet;
    public string Table { get; } = table;
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; } = [];
    /// <summary>Non-fatal issues (e.g. a value truncated to its column's max length) — the row still imported.</summary>
    public List<string> Warnings { get; } = [];
    public List<string> UnmappedColumns { get; } = [];
    public int ResolvedReferences { get; set; }
    public int UnresolvedReferences { get; set; }
}

/// <summary>The full import run report; serializable to <c>import-report.json</c>.</summary>
public sealed class ImportReport
{
    public List<WorksheetReport> Worksheets { get; } = [];

    public int TotalCreated => Worksheets.Sum(w => w.Created);
    public int TotalUpdated => Worksheets.Sum(w => w.Updated);
    public int TotalSkipped => Worksheets.Sum(w => w.Skipped);
    public int TotalWarnings => Worksheets.Sum(w => w.Warnings.Count);

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}
