using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Web.Workspaces;

/// <summary>
/// Host-wide background maintenance for the per-user model: every interval it fast-forwards each active
/// workspace from the shared remote fold (the per-workspace coordinator skips the fold when nothing
/// changed remotely) and evicts idle workspaces; periodically it also deletes working copies of users not
/// seen for the retention period (<c>Workspaces:RetentionDays</c>, default 90) so disk use doesn't grow
/// with every user who ever signed in. Replaces the single-workspace AutoRefreshService.
/// </summary>
public sealed class PerUserAutoRefreshService(
    IWorkspaceRegistry registry,
    WorkspaceLiveness liveness,
    IConfiguration configuration,
    ILogger<PerUserAutoRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxIdle = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan retention = TimeSpan.FromDays(Math.Max(1, configuration.GetValue("Workspaces:RetentionDays", 90)));
        DateTimeOffset nextCleanup = DateTimeOffset.UtcNow; // run once shortly after startup, then every 6h

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

            if (DateTimeOffset.UtcNow >= nextCleanup)
            {
                nextCleanup = DateTimeOffset.UtcNow + CleanupEvery;
                try
                {
                    int deleted = registry.CleanupStaleWorkingCopies(retention);
                    if (deleted > 0)
                    {
                        logger.LogInformation("Deleted {Count} stale working cop(ies) older than {Days} days.", deleted, retention.TotalDays);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Stale working-copy cleanup failed.");
                }
            }
        }
    }
}
