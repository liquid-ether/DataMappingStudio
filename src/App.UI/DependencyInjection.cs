using Microsoft.Extensions.DependencyInjection;

namespace App.UI;

/// <summary>
/// Composition root for the shared Razor UI layer. Registers EN/FR localization
/// (<c>IStringLocalizer</c>, resources under <c>Resources/</c>) so the same components localize in
/// both the Blazor Hybrid desktop shell and the Blazor Server web host.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAppUi(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        return services;
    }
}
