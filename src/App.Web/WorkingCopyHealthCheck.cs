using App.Application.Abstractions;
using App.Application.Sync;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace App.Web;

/// <summary>
/// Liveness/readiness probe for ops: confirms the local working copy is reachable (cheap catalog read)
/// and reports the shared-folder health. Healthy = local OK + remote reachable; Degraded = local OK but
/// the shared folder is unavailable (the app still works locally, publishing is paused); Unhealthy =
/// the local store cannot be read. Exposed anonymously at <c>/health</c>.
/// </summary>
internal sealed class WorkingCopyHealthCheck(ICatalog catalog, ISyncCoordinator sync) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            int tables = catalog.GetTables().Count;
            RemoteHealth remote = sync.RemoteStatus;
            Dictionary<string, object> data = new()
            {
                ["tables"] = tables,
                ["remoteAvailable"] = remote.Available,
                ["remoteLastSuccessUtc"] = remote.LastSuccessUtc?.ToString("O") ?? "never",
            };

            return Task.FromResult(remote.Available
                ? HealthCheckResult.Healthy($"Local store OK ({tables} tables); shared folder reachable.", data)
                : HealthCheckResult.Degraded($"Local store OK; shared folder unavailable: {remote.Message}", data: data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Local working copy is unreachable.", ex));
        }
    }
}
