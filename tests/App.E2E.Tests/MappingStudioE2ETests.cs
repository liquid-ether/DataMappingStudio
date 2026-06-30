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

        // Mappings grid renders the seeded demo model.
        await page.GotoAsync($"{host.BaseUrl}/mappings");
        await WebHostFixture.WaitInteractiveAsync(page);
        await Assertions.Expect(page.Locator(".gridbox")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".gridbox").GetByText("CUSTOMER_360").First).ToBeVisibleAsync();

        // Add a calculated row (exercises an interactive edit); the grid stays rendered.
        await page.Locator("button.add", new() { HasTextString = "Calculated" }).ClickAsync();
        await Assertions.Expect(page.Locator(".gridbox")).ToBeVisibleAsync();

        // Lineage view renders the SVG graph.
        await page.GotoAsync($"{host.BaseUrl}/lineage");
        await WebHostFixture.WaitInteractiveAsync(page);
        await Assertions.Expect(page.Locator("svg").First).ToBeVisibleAsync();
    }
}
