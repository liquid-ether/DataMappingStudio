using App.Application.Abstractions;
using App.Application.Provisioning;
using App.UI.Components;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

/// <summary>
/// The entity editors are the metadata-driven grid pointed at each entity's table — these confirm the
/// default catalog drives them with the right columns/labels in both languages.
/// </summary>
public class EntityEditorTests : AppTestContext
{
    private IRenderedComponent<MetadataGrid> RenderEntity(string table)
    {
        Services.AddSingleton<ICatalog>(new FakeCatalog(DefaultCatalog.Entries()));
        Services.AddSingleton<ILocalStore>(new FakeLocalStore());
        Services.AddSingleton<LanguageState>();
        return Render<MetadataGrid>(p => p.Add(c => c.Table, table));
    }

    [Fact]
    public void Applications_editor_renders_required_app_code()
    {
        var cut = RenderEntity("application");

        Assert.Contains("App code", cut.Markup);
        Assert.Contains("req", cut.Markup);
    }

    [Fact]
    public void Dictionary_editor_renders_its_columns()
    {
        var cut = RenderEntity("dictionary_entry");

        Assert.Contains("Column", cut.Markup);
        Assert.Contains("Data type", cut.Markup);
    }

    [Fact]
    public async Task Labels_switch_to_french_on_toggle()
    {
        var cut = RenderEntity("application");

        await cut.InvokeAsync(() => Services.GetRequiredService<LanguageState>().Set("fr"));

        Assert.Contains("Code applicatif", cut.Markup);
    }
}
