using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Web.Workspaces;

/// <summary>
/// Host-wide background refresh for the per-user model: every interval it fast-forwards each active
/// workspace from the shared remote fold (the per-workspace coordinator skips the fold when nothing
/// changed remotely) and evicts idle workspaces. Replaces the single-workspace AutoRefreshService.
/// </summary>
public sealed class PerUserAutoRefreshService(
    IWorkspaceRegistry registry,
    WorkspaceLiveness liveness,
    ILogger<PerUserAutoRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxIdle = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (Workspace workspace in registry.Active)
            {
                try
                {
                    workspace.Coordinator.Refresh();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Auto-refresh failed for workspace {Writer}.", workspace.WriterId);
                }
            }

            registry.SweepIdle(MaxIdle, liveness.IsActive);
        }
    }
}
