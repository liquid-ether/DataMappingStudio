using App.Application.Abstractions;
using App.Application.Expressions;
using App.Application.Lineage;
using App.Application.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Application;

/// <summary>
/// Composition root for the Application layer (use cases, engines, transport-agnostic services).
/// Hosts (App.Desktop, App.Web, App.Importer) call this plus the infrastructure registrations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();

        // Rule/expression engine + lineage engine (stateless, safe as singletons).
        services.TryAddSingleton<FunctionLibrary>();
        services.TryAddSingleton<ExpressionClassifier>();
        services.TryAddSingleton<RuleExpressionBuilder>();
        services.TryAddSingleton<ILineageEngine, LineageEngine>();

        // Sync engines (pure; the remote store + publish service are wired by AddRemoteStore).
        services.TryAddSingleton<FieldMergeEngine>();
        services.TryAddSingleton<AutoRefreshPlanner>();

        return services;
    }
}
