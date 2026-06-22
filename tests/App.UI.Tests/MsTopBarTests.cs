using App.UI.Components;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

public class MsTopBarTests : BunitContext
{
    public MsTopBarTests() => Services.AddSingleton<LanguageState>();

    [Fact]
    public void Renders_brand_and_publish_in_english_by_default()
    {
        var cut = Render<MsTopBar>();

        Assert.Contains("Mapping Studio", cut.Markup);
        Assert.Contains("Publish", cut.Markup);
    }

    [Fact]
    public void Toggling_to_french_swaps_the_chrome_strings()
    {
        var cut = Render<MsTopBar>();

        cut.FindAll(".ms-lang button")[1].Click(); // FR

        Assert.Contains("Studio de mappage", cut.Markup);
        Assert.Contains("Publier", cut.Markup);
    }

    [Fact]
    public void Publish_button_raises_the_callback()
    {
        bool published = false;
        var cut = Render<MsTopBar>(p => p.Add(c => c.OnPublish, () => published = true));

        cut.Find("button.ms-publish").Click();

        Assert.True(published);
    }
}
