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
        await WebHostFixture.WaitInteractiveAsync(page);
        await page.Locator("button.add", new() { HasTextString = "Add row" }).ClickAsync();
        await page.Locator("input.gi").First.FillAsync("E2EAPP");
        await page.Locator(".addbar button.ms-publish").ClickAsync(); // grid Save

        await page.GotoAsync($"{host.BaseUrl}/publish");
        await WebHostFixture.WaitInteractiveAsync(page);

        // Click Publish until the result message appears (robust against the connect race; re-clicking is
        // idempotent — once published it reports "Already up to date").
        ILocator publishButton = page.Locator(".addbar button.ms-publish"); // the panel's Publish, not the top bar
        ILocator message = page.Locator(".ms-wrap p");
        for (int attempt = 0; attempt < 15; attempt++)
        {
            await publishButton.ClickAsync();
            try { await message.WaitForAsync(new LocatorWaitForOptions { Timeout = 1_000 }); break; }
            catch (TimeoutException) { if (attempt == 14) { throw; } }
        }

        await Assertions.Expect(message).ToBeVisibleAsync();

        // History shows the audit trail.
        await page.GotoAsync($"{host.BaseUrl}/history");
        await Assertions.Expect(page.Locator("table.grid")).ToBeVisibleAsync();
    }
}
