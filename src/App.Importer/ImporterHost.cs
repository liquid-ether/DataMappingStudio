using App.Application;
using App.Infrastructure.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace App.Importer;

/// <summary>
/// Builds the shared .NET Generic Host for the Excel→DB importer, composing the Application and
/// local-store layers (the importer reuses them rather than carrying its own stack). The
/// validate→transform→resolve→load pipeline and the column-mapping facility are added in Phase 9.
/// </summary>
public static class ImporterHost
{
    public static IHost Build(string[]? args = null)
    {
        return Host.CreateApplicationBuilder(args ?? [])
            .ConfigureImporterServices()
            .Build();
    }

    private static HostApplicationBuilder ConfigureImporterServices(this HostApplicationBuilder builder)
    {
        builder.Services
            .AddApplication()
            .AddLocalStore();
        return builder;
    }
}
