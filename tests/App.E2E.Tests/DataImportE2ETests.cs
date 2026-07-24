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
            await WebHostFixture.WaitInteractiveAsync(page);

            // Step 1 — upload the workbook; the wizard parses it and surfaces the file summary. Re-set the
            // file until the circuit's OnChange fires (robust against the connect race), then assert.
            ILocator fileInput = page.Locator("input[type='file']");
            ILocator summary = page.GetByText("import_e2e.xlsx");
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await fileInput.SetInputFilesAsync(workbook);
                try { await summary.WaitForAsync(new LocatorWaitForOptions { Timeout = 1_000 }); break; }
                catch (TimeoutException) { if (attempt == 19) { throw; } }
            }

            await Assertions.Expect(summary).ToBeVisibleAsync();

            // Walk Source → Mapping (prefilled from the built-in template for a team-shaped workbook).
            await page.Locator("button.dbi-next").ClickAsync(); // → Mapping

            // Save the mapping to the shared store — this is what the CLI importer runs by name.
            await page.GetByPlaceholder("Save mapping as").FillAsync("E2E Mapping");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save", Exact = false }).ClickAsync();
            await Assertions.Expect(page.GetByText("'E2E Mapping' saved")).ToBeVisibleAsync();

            // Delete it through the toolbar (rename = save under a new name + delete), then re-save so
            // the rest of the flow — and the shared store — end in the saved state.
            await page.GetByLabel("Saved mappings").SelectOptionAsync("E2E Mapping");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete" }).ClickAsync();
            await Assertions.Expect(page.GetByText("'E2E Mapping' deleted")).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save", Exact = false }).ClickAsync();
            await Assertions.Expect(page.GetByText("'E2E Mapping' saved")).ToBeVisibleAsync();

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

    /// <summary>
    /// The reported repro, end to end: a column added at runtime in /admin/model (with bilingual
    /// labels) must show up in the wizard's Mapping step — offered in the target-column dropdown with
    /// its label, and auto-mapped when a worksheet header matches it.
    /// </summary>
    [SkippableFact]
    public async Task A_runtime_added_column_reaches_the_wizard_with_its_label()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        string workbook = CreateWorkbookWithVendorTier();
        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync();
            IPage page = await browser.NewPageAsync();

            // Add a labeled column to 'application' through the meta-model admin.
            await page.GotoAsync($"{host.BaseUrl}/admin/model");
            await WebHostFixture.WaitInteractiveAsync(page);
            await page.Locator("tr", new() { HasTextString = "application" }).First
                .Locator("button.add", new() { HasTextString = "Add column" }).ClickAsync();
            await page.GetByPlaceholder("column_name").FillAsync("vendor_tier");
            await page.GetByPlaceholder("Label (EN)").FillAsync("Vendor tier");
            await page.GetByPlaceholder("Label (FR)").FillAsync("Niveau fournisseur");
            await page.Locator("button.ms-publish", new() { HasTextString = "Save" }).ClickAsync();
            await Assertions.Expect(page.GetByText("Column 'vendor_tier' added.")).ToBeVisibleAsync();

            // Upload a workbook whose Apps sheet carries a matching header and open the Mapping step.
            await page.GotoAsync($"{host.BaseUrl}/import");
            await WebHostFixture.WaitInteractiveAsync(page);
            ILocator fileInput = page.Locator("input[type='file']");
            ILocator summary = page.GetByText("import_e2e_tier.xlsx");
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await fileInput.SetInputFilesAsync(workbook);
                try { await summary.WaitForAsync(new LocatorWaitForOptions { Timeout = 1_000 }); break; }
                catch (TimeoutException) { if (attempt == 19) { throw; } }
            }

            await page.Locator("button.dbi-next").ClickAsync(); // → Mapping

            // The header auto-mapped to the runtime column, and the option shows the bilingual label.
            ILocator select = page.GetByLabel("Target column — Vendor tier");
            await Assertions.Expect(select).ToHaveValueAsync("vendor_tier");
            await Assertions.Expect(select).ToContainTextAsync("Vendor tier (vendor_tier)");
        }
        finally
        {
            try { File.Delete(workbook); } catch (IOException) { }
        }
    }

    private static string CreateWorkbookWithVendorTier()
    {
        string path = Path.Combine(Path.GetTempPath(), "import_e2e_tier.xlsx");
        using XLWorkbook wb = new();
        IXLWorksheet apps = wb.Worksheets.Add("Apps");
        apps.Cell(1, 1).Value = "AppCode";
        apps.Cell(1, 2).Value = "Nom GDM";
        apps.Cell(1, 3).Value = "Vendor tier";
        apps.Cell(2, 1).Value = "E2ETIER";
        apps.Cell(2, 2).Value = "Tiered App";
        apps.Cell(2, 3).Value = "gold";
        wb.SaveAs(path);
        return path;
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
