using App.UI;
using Bunit;

namespace App.UI.Tests;

public class ComponentSmokeTests : BunitContext
{
    [Fact]
    public void Shared_component_renders()
    {
        var cut = Render<Component1>();

        Assert.Contains("App.UI", cut.Markup);
    }
}
