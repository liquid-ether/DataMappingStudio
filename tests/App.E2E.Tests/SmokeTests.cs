using Microsoft.Playwright;

namespace App.E2E.Tests;

/// <summary>
/// Phase 0 placeholder. Real browser-driven flows (author → trace → publish → resolve → audit)
/// run against the Blazor Server <c>App.Web</c> host and arrive in Phase 5. This only proves the
/// Playwright package is wired so no browser download is required in CI yet.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void Playwright_package_is_referenced()
    {
        Assert.Equal("Microsoft.Playwright", typeof(IPlaywright).Assembly.GetName().Name);
    }
}
