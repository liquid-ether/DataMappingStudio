using System.Globalization;
using App.UI;
using App.UI.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace App.UI.Tests;

public class LocalizationSmokeTests
{
    [Theory]
    [InlineData("en", "Publish")]
    [InlineData("fr", "Publier")]
    public void Shared_strings_localize_for_en_and_fr(string culture, string expected)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddAppUi()
            .BuildServiceProvider();
        IStringLocalizer<SharedResource> localizer = provider.GetRequiredService<IStringLocalizer<SharedResource>>();

        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            LocalizedString value = localizer["Publish"];

            Assert.False(value.ResourceNotFound);
            Assert.Equal(expected, value.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
