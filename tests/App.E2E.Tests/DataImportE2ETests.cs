using ClosedXML.Excel;
using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// Drives the Data Import wizard through a real browser: upload an .xlsx, step through
/// Source → Mapping → Validate → Load, run the import, and confirm the recap reports the result — proving
/// the wizard performs an end-to-end import into the local store via the UI. Skips unless <c>DMS_E2E=1</c>.
/// </summary>
public sealed class DataImportE2ETests(WebHostFixture host) : IClassFixture<WebHostFixture>
{
    [SkippableFact]
    public async Task Importing_a_workbook_through_the_wizard_loads_rows()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        string workbook = CreateWorkbook();
        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync();
            IPage page = await browser.NewPageAsync();

            await page.GotoAsync($"{host.BaseUrl}/import");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            // Step 1 — upload the workbook; the wizard parses it and surfaces the file summary.
            await page.Locator("input[type='file']").SetInputFilesAsync(workbook);
            await Assertions.Expect(page.GetByText("import_e2e.xlsx")).ToBeVisibleAsync();

            // Walk Source → Mapping → Validate → Load (Next auto-waits until each step is actionable).
            await page.Locator("button.dbi-next").ClickAsync(); // → Mapping
            await page.Locator("button.dbi-next").ClickAsync(); // → Validate
            await page.Locator("button.dbi-next").ClickAsync(); // → Load

            // Run the real import.
            await page.Locator("button.dbi-bigbtn").ClickAsync();

            // Recap shows the outcome, and the per-worksheet table lists the target tables.
            await Assertions.Expect(page.GetByText("Import complete")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("application").First).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("data_source").First).ToBeVisibleAsync();
        }
        finally
        {
            try { File.Delete(workbook); } catch (IOException) { }
        }
    }

    // A minimal team-shaped workbook: an Apps sheet and a Source sheet whose application_id resolves to
    // the app by natural key during the same import.
    private static string CreateWorkbook()
    {
        string path = Path.Combine(Path.GetTempPath(), "import_e2e.xlsx");
        using XLWorkbook wb = new();

        IXLWorksheet apps = wb.Worksheets.Add("Apps");
        apps.Cell(1, 1).Value = "AppCode";
        apps.Cell(1, 2).Value = "Nom GDM";
        apps.Cell(2, 1).Value = "E2EAPP";
        apps.Cell(2, 2).Value = "E2E Imported App";

        IXLWorksheet source = wb.Worksheets.Add("Source");
        source.Cell(1, 1).Value = "Nom";
        source.Cell(1, 2).Value = "Code applicatif du système";
        source.Cell(2, 1).Value = "E2E Orders";
        source.Cell(2, 2).Value = "E2EAPP";

        wb.SaveAs(path);
        return path;
    }
}
