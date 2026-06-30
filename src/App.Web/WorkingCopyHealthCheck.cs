using App.Application.Abstractions;
using App.Web.Workspaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace App.Web;

/// <summary>
/// Ops probe at <c>/health</c> for the per-user web host: confirms the shared folder is reachable (cheap
/// signature read) and reports how many user workspaces are live on this host. Healthy = shared folder
/// reachable; Degraded = shared folder unreachable (users can still work locally, publishing is paused);
/// Unhealthy = unexpected failure. Uses only singletons, so it never creates a workspace.
/// </summary>
internal sealed class WorkingCopyHealthCheck(IWorkspaceRegistry registry, IRemoteStore remote) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        int active = registry.Active.Count;
        try
        {
            _ = remote.RemoteVersion(); // cheap stat of the shared logs; throws if the folder is unreachable
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Shared folder reachable; {active} active workspace(s).",
                new Dictionary<string, object> { ["activeWorkspaces"] = active }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Shared folder unreachable; {active} active workspace(s). {ex.Message}",
                data: new Dictionary<string, object> { ["activeWorkspaces"] = active }));
        }
    }
}
