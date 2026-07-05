using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// The runtime-settings admin in a real browser (guest mode = full admin): change a shared setting,
/// see the confirmation, and verify it persisted across a reload. Skips unless <c>DMS_E2E=1</c>.
/// </summary>
public sealed class SettingsE2ETests(WebHostFixture host) : IClassFixture<WebHostFixture>
{
    [SkippableFact]
    public async Task Changing_a_shared_setting_persists_across_a_reload()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync($"{host.BaseUrl}/admin/settings");
        await WebHostFixture.WaitInteractiveAsync(page);

        // The first shared row is Sync.AutoRefreshSeconds; set it to 45 and save.
        ILocator row = page.Locator("tr", new() { Has = page.GetByText("Sync.AutoRefreshSeconds") });
        await row.Locator("input").FillAsync("45");
        await row.Locator("button.add").ClickAsync();
        await Assertions.Expect(page.GetByText("'Sync.AutoRefreshSeconds' saved.")).ToBeVisibleAsync();

        // Survives a full reload (read back from the shared settings document).
        await page.ReloadAsync();
        await WebHostFixture.WaitInteractiveAsync(page);
        await Assertions.Expect(page.Locator("tr", new() { Has = page.GetByText("Sync.AutoRefreshSeconds") }).Locator("input"))
            .ToHaveValueAsync("45");
    }
}
