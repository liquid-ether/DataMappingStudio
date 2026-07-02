using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// RBAC end to end: an admin creates a Reader through the admin UI; signed in as that Reader, the
/// gated operations (publish, grid editing, administration) are absent or denied — both in the UI and on
/// hard navigation (server-enforced). Skips unless <c>DMS_E2E=1</c>.
/// </summary>
public sealed class ReaderRoleE2ETests(AuthWebHostFixture host) : IClassFixture<AuthWebHostFixture>
{
    private const string ReaderUser = "reader1";
    private const string ReaderPassword = "Reader!Passw0rd1";

    [SkippableFact]
    public async Task A_reader_cannot_publish_edit_or_administer()
    {
        Skip.IfNot(WebHostFixture.Enabled, "Set DMS_E2E=1 (and run 'playwright install chromium') to run E2E.");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();

        // Admin signs in and creates a Reader through the admin Users UI (role dropdown defaults to Reader).
        await SignInAsync(page, AuthWebHostFixture.AdminUser, AuthWebHostFixture.AdminPassword);
        await page.GotoAsync($"{host.BaseUrl}/admin/users");
        await WebHostFixture.WaitInteractiveAsync(page);
        await page.Locator("button.add", new() { HasTextString = "New user" }).ClickAsync();
        await page.GetByPlaceholder("Username").FillAsync(ReaderUser);
        await page.GetByPlaceholder("Password").FillAsync(ReaderPassword);
        await page.Locator(".draftbar button.ms-publish").ClickAsync();
        await Assertions.Expect(page.GetByText("User created.")).ToBeVisibleAsync();

        // Sign out, sign in as the Reader.
        await page.Locator("form[action$='account/logout'] button").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/account/login"));
        await SignInAsync(page, ReaderUser, ReaderPassword);
        await WebHostFixture.WaitInteractiveAsync(page);

        // No Publish anywhere in the chrome; no add-row on the grid (Data.Edit not granted).
        await Assertions.Expect(page.Locator("button.ms-publish")).ToHaveCountAsync(0);
        await page.GotoAsync($"{host.BaseUrl}/t/application");
        await WebHostFixture.WaitInteractiveAsync(page);
        await Assertions.Expect(page.Locator("button.add", new() { HasTextString = "Add row" })).ToHaveCountAsync(0);

        // Hard navigation to the admin area is denied server-side (policy on the endpoint).
        await page.GotoAsync($"{host.BaseUrl}/admin/users");
        await Assertions.Expect(page.GetByText("Access denied")).ToBeVisibleAsync();
    }

    private async Task SignInAsync(IPage page, string user, string password)
    {
        await page.GotoAsync($"{host.BaseUrl}/account/login");
        await page.FillAsync("#username", user);
        await page.FillAsync("#password", password);
        await page.ClickAsync("button[type=submit]");
        await Assertions.Expect(page.Locator(".ms-brand").First).ToContainTextAsync("Mapping Studio");
    }
}
