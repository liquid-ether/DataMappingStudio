using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// Browser-driven smoke flow against the real Blazor Server host. Skips unless <c>DMS_E2E=1</c>
/// (CI sets it after installing Playwright browsers). Validates load, the instant EN/FR toggle, and
/// navigation to the metadata-driven grid.
/// </summary>
public sealed class SmokeE2ETests(WebHostFixture host) : IClassFixture<WebHostFixture>
{
    [SkippableFact]
    public async Task Home_loads_toggles_language_and_navigates_to_the_grid()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync(host.BaseUrl);
        await WebHostFixture.WaitInteractiveAsync(page);
        await Assertions.Expect(page.Locator(".ms-brand")).ToContainTextAsync("Mapping Studio");

        // Instant EN/FR toggle.
        await page.Locator(".ms-lang button", new() { HasTextString = "FR" }).ClickAsync();
        await Assertions.Expect(page.Locator(".ms-brand")).ToContainTextAsync("Studio de mappage");

        // Navigate to a real metadata-driven grid and confirm a catalog column renders.
        await page.GotoAsync($"{host.BaseUrl}/t/application");
        await Assertions.Expect(page.GetByText("App code").First).ToBeVisibleAsync();
    }
}
