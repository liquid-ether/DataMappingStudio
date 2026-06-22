using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// Mapping Studio + Lineage flow against the real host. Skips unless <c>DMS_E2E=1</c>.
/// </summary>
public sealed class MappingStudioE2ETests(WebHostFixture host) : IClassFixture<WebHostFixture>
{
    [SkippableFact]
    public async Task Author_a_field_then_trace_its_lineage()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();

        // Mappings grid renders the demo with grouped targets + source chips.
        await page.GotoAsync($"{host.BaseUrl}/mappings");
        await Assertions.Expect(page.Locator("table.grid")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("a:CRM_ACCOUNTS")).ToBeVisibleAsync();

        // Add a calculated row.
        await page.Locator("button.add", new() { HasTextString = "Calculated" }).ClickAsync();
        await Assertions.Expect(page.GetByText("new_indicator")).ToBeVisibleAsync();

        // Lineage view renders the SVG graph and the unresolved-reference note.
        await page.GotoAsync($"{host.BaseUrl}/lineage");
        await Assertions.Expect(page.Locator("svg")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".unres-note")).ToBeVisibleAsync();
    }
}
