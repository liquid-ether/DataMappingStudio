using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>Publish + history pages smoke. Skips unless <c>DMS_E2E=1</c>.</summary>
public sealed class PublishE2ETests(WebHostFixture host) : IClassFixture<WebHostFixture>
{
    [SkippableFact]
    public async Task Publish_and_history_pages_work()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();

        // Make a local edit, then publish it.
        await page.GotoAsync($"{host.BaseUrl}/t/application");
        await page.Locator("button.add", new() { HasTextString = "Add row" }).ClickAsync();
        await page.Locator("input.gi").First.FillAsync("E2EAPP");
        await page.Locator("button.ms-publish").ClickAsync();

        await page.GotoAsync($"{host.BaseUrl}/publish");
        await page.Locator("button.ms-publish").First.ClickAsync();
        await Assertions.Expect(page.Locator(".ms-wrap p")).ToBeVisibleAsync();

        // History shows the audit trail.
        await page.GotoAsync($"{host.BaseUrl}/history");
        await Assertions.Expect(page.Locator("table.grid")).ToBeVisibleAsync();
    }
}
