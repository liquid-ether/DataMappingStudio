using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// Entity-editor flow: create an Application and confirm the value persists across a reload (it was
/// written to the local store). Skips unless <c>DMS_E2E=1</c>.
/// </summary>
public sealed class EntityFlowE2ETests(WebHostFixture host) : IClassFixture<WebHostFixture>
{
    [SkippableFact]
    public async Task Creating_an_application_persists_it()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync($"{host.BaseUrl}/t/application");
        await WebHostFixture.WaitInteractiveAsync(page);

        await page.Locator("button.add", new() { HasTextString = "Add row" }).ClickAsync();
        await page.Locator("input.gi").First.FillAsync("MYAPP");
        await page.Locator(".addbar button.ms-publish").ClickAsync(); // the grid's Save (not the top bar's Publish)

        // Reload from the local store and confirm it persisted (cell values live in <input>s).
        await page.ReloadAsync();
        await WebHostFixture.WaitInteractiveAsync(page);
        await Assertions.Expect(page.Locator("input.gi").First).ToHaveValueAsync("MYAPP");
    }
}
