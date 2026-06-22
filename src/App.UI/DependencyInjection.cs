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
        return services;
    }
}
