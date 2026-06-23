using App.Application;
using App.Application.Abstractions;
using App.Application.Mappings;
using App.Application.Provisioning;
using App.Application.References;
using App.Application.Sync;
using App.Infrastructure.Local;
using App.UI;
using App.UI.Localization;
using App.UI.MappingStudio;
using Bunit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

/// <summary>
/// Renders the Blazor Hybrid desktop root component with the same service shape the desktop host wires,
/// to reproduce/guard the desktop's component tree (the web host is exercised separately).
/// </summary>
public class DesktopRootTests : BunitContext
{
    [Fact]
    public void Desktop_root_renders_without_error()
    {
        Services.AddSingleton<ICatalog>(new FakeCatalog(DefaultCatalog.Entries()));
        Services.AddSingleton<ILocalStore>(new FakeLocalStore());
        Services.AddSingleton<LanguageState>();
        Services.AddSingleton<ReferenceService>();
        Services.AddSingleton<IReferenceResolver>(sp => sp.GetRequiredService<ReferenceService>());
        Services.AddSingleton<IComputedEvaluator>(sp => sp.GetRequiredService<ReferenceService>());

        var cut = Render<DesktopRoot>();

        Assert.Contains("Mapping Studio", cut.Markup);
        Assert.Contains("App code", cut.Markup); // the application editor's first column header
    }

    [Fact]
    public void Desktop_root_renders_with_the_real_sqlite_store_and_provisioning()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dms-desktoproot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Mirror the desktop host: real SQLite store + DefaultCatalog provisioning + reference services.
            LocalDatabase db = new($"Data Source={Path.Combine(dir, "local.db")}");
            SqliteCatalog catalog = new(db);
            catalog.Seed(DefaultCatalog.Entries());
            SqliteLocalStore store = new(db, catalog, new SqliteAuditLog(db), new App.UI.Tests.SystemClock());
            store.EnsureSchema();

            Services.AddSingleton(db);
            Services.AddSingleton<ICatalog>(catalog);
            Services.AddSingleton<ILocalStore>(store);
            Services.AddSingleton<LanguageState>();
            Services.AddSingleton<ReferenceService>();
            Services.AddSingleton<IReferenceResolver>(sp => sp.GetRequiredService<ReferenceService>());
            Services.AddSingleton<IComputedEvaluator>(sp => sp.GetRequiredService<ReferenceService>());

            var cut = Render<DesktopRoot>();

            Assert.Contains("App code", cut.Markup);
            db.Dispose();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Wires the full desktop service graph so every tab's view can render.</summary>
    private void RegisterFullDesktopServices()
    {
        Services.AddApplication(); // FunctionLibrary, ExpressionClassifier, ILineageEngine
        Services.AddSingleton<ICatalog>(new FakeCatalog(DefaultCatalog.Entries()));
        Services.AddSingleton<ILocalStore>(new FakeLocalStore());
        Services.AddSingleton<LanguageState>();
        Services.AddSingleton<IMappingRepository, MappingRepository>();
        Services.AddSingleton<IMappingTargetRepository, MappingTargetRepository>();
        Services.AddSingleton<MappingStudioState>();
        Services.AddSingleton<ReferenceService>();
        Services.AddSingleton<IReferenceResolver>(sp => sp.GetRequiredService<ReferenceService>());
        Services.AddSingleton<IComputedEvaluator>(sp => sp.GetRequiredService<ReferenceService>());
        Services.AddSingleton<ISyncCoordinator>(new FakeSyncCoordinator());
    }

    [Fact]
    public void Every_menu_tab_switches_to_its_view()
    {
        RegisterFullDesktopServices();

        var cut = Render<DesktopRoot>();
        LanguageState lang = Services.GetRequiredService<LanguageState>();

        void ClickTab(string label) =>
            cut.FindAll(".ms-tabs button").First(b => b.TextContent.Trim() == label).Click();

        // Default view: the first entity grid (Applications), proven via its localized page title.
        Assert.Contains($"page-title\">{lang.T("navApplications")}</h2>", cut.Markup);
        Assert.Contains("App code", cut.Markup);

        // Every entity tab switches to its catalog-driven grid (asserted via the localized title).
        foreach (string navKey in new[] { "navSources", "navDictionary", "navConfig", "navClassification", "navRules" })
        {
            ClickTab(lang.T(navKey));
            Assert.Contains($"page-title\">{lang.T(navKey)}</h2>", cut.Markup);
        }

        // Mapping Studio tab.
        ClickTab(lang.T("tabMappings"));
        Assert.Contains("CUSTOMER_360", cut.Markup);

        // Lineage tab.
        ClickTab(lang.T("tabLineage"));
        Assert.Contains("<svg", cut.Markup);
        Assert.Contains("BILLING", cut.Markup);

        // History tab.
        ClickTab(lang.T("navHistory"));
        Assert.Contains(">History<", cut.Markup);
        Assert.Contains(">Op<", cut.Markup);

        // Publish is the top-bar button (not a tab); it shows the publish panel.
        cut.Find(".ms-top button.ms-publish").Click();
        Assert.Contains($"page-title\">{lang.T("publish")}</h1>", cut.Markup);
        Assert.Contains("Refresh", cut.Markup);

        // Back to an entity tab to confirm round-tripping.
        ClickTab(lang.T("navApplications"));
        Assert.Contains($"page-title\">{lang.T("navApplications")}</h2>", cut.Markup);
    }

    [Fact]
    public void Language_toggle_switches_the_active_view_to_french()
    {
        Services.AddSingleton<ICatalog>(new FakeCatalog(DefaultCatalog.Entries()));
        Services.AddSingleton<ILocalStore>(new FakeLocalStore());
        Services.AddSingleton<LanguageState>();
        Services.AddSingleton<ReferenceService>();
        Services.AddSingleton<IReferenceResolver>(sp => sp.GetRequiredService<ReferenceService>());
        Services.AddSingleton<IComputedEvaluator>(sp => sp.GetRequiredService<ReferenceService>());

        var cut = Render<DesktopRoot>();
        LanguageState lang = Services.GetRequiredService<LanguageState>();

        // Switch to the Dictionary editor, whose label differs between EN ("Dictionary") and FR ("Dictionnaire").
        cut.FindAll(".ms-tabs button").First(b => b.TextContent.Trim() == lang.T("navDictionary")).Click();
        Assert.Contains("page-title\">Dictionary</h2>", cut.Markup);

        cut.FindAll(".ms-lang button").First(b => b.TextContent.Trim() == "FR").Click();

        Assert.True(lang.IsFrench);
        Assert.Contains("page-title\">Dictionnaire</h2>", cut.Markup); // active view re-rendered in French
    }
}

/// <summary>Trivial clock for the real-store render test.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
