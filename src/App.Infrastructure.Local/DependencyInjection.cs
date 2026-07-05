using App.Application.Abstractions;
using App.Application.Importing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Infrastructure.Local;

/// <summary>
/// Composition root for the local store (generic Microsoft.Data.Sqlite repository, column catalog,
/// local change log / audit). Pass the path to the analyst's SQLite working copy to wire the
/// concrete services; with no path the seam is a no-op (e.g. hosts that have no DB configured yet).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddLocalStore(this IServiceCollection services, string? databasePath = null)
    {
        // The workbook reader (Excel) needs no database, so register it even with no DB path configured.
        services.TryAddSingleton<IWorkbookReader, ClosedXmlWorkbookReader>();

        if (databasePath is null)
        {
            return services;
        }

        services.TryAddSingleton(_ => new LocalDatabase($"Data Source={databasePath}"));
        services.TryAddSingleton<SqliteCatalog>();
        services.TryAddSingleton<ICatalog>(sp => sp.GetRequiredService<SqliteCatalog>());
        services.TryAddSingleton<ITableCatalog>(sp => sp.GetRequiredService<SqliteCatalog>()); // same instance serves both meta tables
        services.TryAddSingleton<IAuditLog, SqliteAuditLog>();
        services.TryAddSingleton<ILocalStore, SqliteLocalStore>();
        return services;
    }
}
