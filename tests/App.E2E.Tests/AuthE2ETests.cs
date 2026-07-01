using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// The authenticated flow end to end against a real host with <c>Auth:Require=true</c>: an anonymous
/// visitor is redirected to sign in, the seeded admin can log in and reach the app (with the account +
/// Admin controls), and sign-out returns to the login page. Skips unless <c>DMS_E2E=1</c>.
/// </summary>
public sealed class AuthE2ETests(AuthWebHostFixture host) : IClassFixture<AuthWebHostFixture>
{
    [SkippableFact]
    public async Task Anonymous_is_redirected_admin_signs_in_and_signs_out()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();

        // Anonymous visitors are bounced to the sign-in page by the fallback authorization policy.
        await page.GotoAsync(host.BaseUrl);
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/account/login"));

        // Sign in with the seeded bootstrap admin (a real browser carries the antiforgery cookie + token).
        await page.FillAsync("#username", AuthWebHostFixture.AdminUser);
        await page.FillAsync("#password", AuthWebHostFixture.AdminPassword);
        await page.ClickAsync("button[type=submit]");

        // Lands in the app; the account area (only rendered when signed in) shows the sign-out control.
        await Assertions.Expect(page.Locator(".ms-brand")).ToContainTextAsync("Mapping Studio");
        await WebHostFixture.WaitInteractiveAsync(page);
        ILocator signOut = page.Locator("form[action$='account/logout'] button");
        await Assertions.Expect(signOut).ToBeVisibleAsync();

        // The Administrator can reach the user-management admin page.
        await page.GotoAsync($"{host.BaseUrl}/admin/users");
        await Assertions.Expect(page.GetByText("Users").First).ToBeVisibleAsync();

        // Sign out returns to the login page.
        await page.GotoAsync(host.BaseUrl);
        await WebHostFixture.WaitInteractiveAsync(page);
        await page.Locator("form[action$='account/logout'] button").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/account/login"));
    }
}
