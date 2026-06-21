using App.Application.Abstractions;
using App.Importer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace App.Importer.Tests;

public class SmokeTests
{
    [Fact]
    public void ImporterHost_composes_application_and_local_store()
    {
        using IHost host = ImporterHost.Build([]);

        // The importer reuses the Application layer; resolving a core service proves composition.
        IClock clock = host.Services.GetRequiredService<IClock>();

        Assert.NotNull(clock);
    }
}
