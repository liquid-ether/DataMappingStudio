using App.UI;
using App.UI.Components;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

public class MsSideNavTests : AppTestContext
{
    private static readonly IReadOnlyList<NavSection> Sections =
    [
        new NavSection("Data model", "Modèle de données",
            [new NavItem("t/application", "navApplications"), new NavItem("t/data_source", "navSources")]),
        new NavSection("Tools", "Outils", [new NavItem("history", "navHistory")]),
    ];

    public MsSideNavTests() => Services.AddSingleton<LanguageState>();

    [Fact]
    public void Renders_sections_and_items_and_raises_selection()
    {
        string? selected = null;
        var cut = Render<MsSideNav>(p => p
            .Add(x => x.Sections, Sections)
            .Add(x => x.ActiveHref, "t/application")
            .Add(x => x.OnSelect, (string href) => selected = href));

        Assert.Contains("Data model", cut.Markup);
        Assert.Contains("Tools", cut.Markup);

        var items = cut.FindAll("button.navitem");
        Assert.Equal(4, items.Count); // 3 destinations + the collapse control
        Assert.Equal("true", items[0].GetAttribute("aria-selected")); // active item marked

        items[1].Click();
        Assert.Equal("t/data_source", selected);
    }

    [Fact]
    public void Collapse_toggle_switches_to_the_icon_rail_and_back()
    {
        var cut = Render<MsSideNav>(p => p.Add(x => x.Sections, Sections).Add(x => x.OnSelect, (string _) => { }));

        Assert.DoesNotContain("collapsed", cut.Find("nav.ms-nav").ClassList);

        cut.FindAll("button.navitem").Last().Click(); // the collapse control
        Assert.Contains("collapsed", cut.Find("nav.ms-nav").ClassList);
        Assert.Equal("false", cut.FindAll("button.navitem").Last().GetAttribute("aria-expanded"));

        cut.FindAll("button.navitem").Last().Click();
        Assert.DoesNotContain("collapsed", cut.Find("nav.ms-nav").ClassList);
    }

    [Fact]
    public void Routed_mode_renders_navlinks_when_no_selection_callback_is_supplied()
    {
        var cut = Render<MsSideNav>(p => p.Add(x => x.Sections, Sections));

        Assert.Equal(3, cut.FindAll("a.navitem").Count);
        Assert.Contains(cut.FindAll("a.navitem"), a => a.GetAttribute("href") == "t/application");
    }
}
