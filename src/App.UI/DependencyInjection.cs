using App.Application.Catalog;
using App.Application.Mappings;
using App.Application.References;
using App.UI.Localization;
using App.UI.MappingStudio;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI;

/// <summary>
/// Composition root for the shared Razor UI layer. Registers EN/FR localization (the resx
/// <c>IStringLocalizer</c> path, plus the instant-toggle <see cref="LanguageState"/> used by the
/// components) so the same components localize in both the Blazor Hybrid desktop shell and the Blazor
/// Server web host.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAppUi(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddScoped<LanguageState>();
        services.AddScoped<MappingStudioState>();

        // Reference pickers + computed-column (autofill) evaluation over the local store.
        services.AddScoped<ReferenceService>();
        services.AddScoped<IReferenceResolver>(sp => sp.GetRequiredService<ReferenceService>());
        services.AddScoped<IComputedEvaluator>(sp => sp.GetRequiredService<ReferenceService>());

        // Real data-model queries (data sources + their dictionary fields) driving the studio + lineage.
        services.AddScoped<ICatalogQuery, CatalogQuery>();

        // Mapping Studio rows + target/source alias structure persist to the local store (so edits sync).
        services.AddScoped<IMappingRepository, MappingRepository>();
        services.AddScoped<IMappingTargetRepository, MappingTargetRepository>();
        return services;
    }
}
