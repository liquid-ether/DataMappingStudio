using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// The meta-model admin end to end in a real browser (guest mode = full admin): create a table from
/// /admin/model, see it appear in the side navigation, open it and add a row. The multi-user sync half
/// (a second working copy receiving the table + data) is covered by CatalogSyncTests without a browser.
/// Skips unless <c>DMS_E2E=1</c>.
/// </summary>
public sealed class MetaModelE2ETests(WebHostFixture host) : IClassFixture<WebHostFixture>
{
    [SkippableFact]
    public async Task Creating_a_table_in_the_admin_puts_it_in_the_nav_and_makes_it_editable()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync($"{host.BaseUrl}/admin/model");
        await WebHostFixture.WaitInteractiveAsync(page);

        // Create a "vendor" table with one required text column, nav-visible.
        await page.Locator("button.add", new() { HasTextString = "New table" }).ClickAsync();
        await page.GetByPlaceholder("table_name").FillAsync("vendor");
        // First Label inputs = the TABLE labels; the column editor below has its own pair.
        await page.GetByPlaceholder("Label (EN)").First.FillAsync("Vendors");
        await page.GetByPlaceholder("Label (FR)").First.FillAsync("Fournisseurs");
        await page.GetByPlaceholder("column_name").FillAsync("code");
        await page.Locator("button.ms-publish", new() { HasTextString = "Create table" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Table 'vendor' created.")).ToBeVisibleAsync();

        // The nav rebuilds per navigation: after moving anywhere, the new table shows in the side menu.
        await page.GotoAsync(host.BaseUrl);
        await WebHostFixture.WaitInteractiveAsync(page);
        await Assertions.Expect(page.Locator(".ms-nav .navitem[title='Vendors']")).ToBeVisibleAsync();

        // Open it and add a row through the ordinary metadata grid.
        await page.Locator(".ms-nav .navitem[title='Vendors']").ClickAsync();
        await WebHostFixture.WaitInteractiveAsync(page);
        await page.Locator("button.add", new() { HasTextString = "Add row" }).First.ClickAsync();
        await page.Locator("input.gi").First.FillAsync("V-001");
        await page.Locator(".addbar button.ms-publish").ClickAsync(); // grid Save
        await Assertions.Expect(page.Locator("input.gi").First).ToHaveValueAsync("V-001");
    }
}
