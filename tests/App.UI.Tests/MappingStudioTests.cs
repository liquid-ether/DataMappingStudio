using App.Application;
using App.Application.Abstractions;
using App.Application.Catalog;
using App.Application.Expressions;
using App.Application.Mappings;
using App.UI.Components;
using App.UI.Localization;
using App.UI.MappingStudio;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

public class MappingStudioTests : AppTestContext
{
    public MappingStudioTests()
    {
        Services.AddApplication(); // FunctionLibrary, ExpressionClassifier, ILineageEngine
        Services.AddSingleton<LanguageState>();
        Services.AddSingleton<ILocalStore>(new FakeLocalStore());
        Services.AddSingleton<ICatalogQuery, CatalogQuery>();
        Services.AddSingleton<IMappingRepository, MappingRepository>();
        Services.AddSingleton<IMappingTargetRepository, MappingTargetRepository>();
        Services.AddSingleton<MappingStudioState>();
    }

    private static IReadOnlyList<KnownReference> Customer360Refs()
    {
        FakeLocalStore store = new();
        return new MappingStudioState(new MappingRepository(store), new MappingTargetRepository(store), new CatalogQuery(store), new App.Application.Abstractions.EnvironmentCurrentUser()).KnownReferences("CUSTOMER_360");
    }

    [Fact]
    public void Expression_view_highlights_functions_fields_and_literals()
    {
        var cut = Render<ExpressionView>(p => p
            .Add(c => c.Expression, "CONCAT(a.first_name,' ',a.last_name)")
            .Add(c => c.Refs, Customer360Refs()));

        Assert.Contains("class=\"fn\">CONCAT", cut.Markup);
        Assert.Contains("class=\"chip a\">a.first_name", cut.Markup);
        Assert.Contains("class=\"lit\"", cut.Markup); // the ' ' string literal (apostrophe encoding varies)
    }

    [Fact]
    public void Mapping_grid_lists_rows_with_target_and_kind()
    {
        var cut = Render<MappingGrid>();

        Assert.Contains("CUSTOMER_360", cut.Markup);         // target column
        Assert.Contains("15 mappings", cut.Markup);          // demo row count
        Assert.Contains("kind-badge join", cut.Markup);      // the join row badge
    }

    [Fact]
    public void Adding_a_field_row_appears_in_the_grid()
    {
        var cut = Render<MappingGrid>();

        cut.FindAll("button.add").First(b => b.TextContent.Contains("Field", StringComparison.Ordinal)).Click();

        Assert.Contains("new_field", cut.Markup);
        Assert.Contains("16 mappings", cut.Markup);
    }

    [Fact]
    public void Lineage_view_scopes_to_a_source_and_flags_unresolved()
    {
        var cut = Render<LineageView>();

        // Defaults to the first source (BILLING) and draws its scoped, both-ways graph.
        Assert.Contains("<svg", cut.Markup);
        Assert.Contains("BILLING", cut.Markup);              // selected source card
        Assert.Contains("CUSTOMER_360", cut.Markup);         // a target it feeds (downstream)
        Assert.Contains("unresolved", cut.Markup);           // the b.plan_label note

        // Clicking a field highlights its upstream closure.
        cut.FindAll("g.node").First().Click();
        Assert.Contains("hl-node", cut.Markup);
    }

    [Fact]
    public void Expression_editor_offers_autocomplete_for_a_typed_token()
    {
        var cut = Render<ExpressionEditor>(p => p
            .Add(c => c.Expression, "a.acct_id")
            .Add(c => c.Refs, Customer360Refs()));

        cut.Find(".cell.editable").Click();        // enter edit mode
        cut.Find("input.ed-input").Input("a.acc"); // type a partial token

        Assert.Contains("ac-item", cut.Markup);
        Assert.Contains("a.acct_id", cut.Markup);
    }
}
