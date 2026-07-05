using App.Application.Abstractions;
using App.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Web.Workspaces;

/// <summary>
/// Host-wide background maintenance for the per-user model: every interval it fast-forwards each active
/// workspace from the shared remote fold (the per-workspace coordinator skips the fold when nothing
/// changed remotely) and evicts idle workspaces; periodically it also deletes working copies of users not
/// seen for the retention period so disk use doesn't grow with every user who ever signed in. The refresh
/// interval, idle threshold and retention are live runtime settings (Sync.AutoRefreshSeconds,
/// Workspaces.MaxIdleMinutes, Workspaces.RetentionDays), read per tick so admin changes apply without a
/// restart; Workspaces:RetentionDays in appsettings remains the fallback default.
/// </summary>
public sealed class PerUserAutoRefreshService(
    IWorkspaceRegistry registry,
    WorkspaceLiveness liveness,
    IConfiguration configuration,
    ILogger<PerUserAutoRefreshService> logger,
    IRuntimeConfig? runtimeConfig = null) : BackgroundService
{
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int fallbackRetention = Math.Max(1, configuration.GetValue("Workspaces:RetentionDays", 90));
        DateTimeOffset nextCleanup = DateTimeOffset.UtcNow; // run once shortly after startup, then every 6h

        while (!stoppingToken.IsCancellationRequested)
        {
            int intervalSeconds = Math.Max(5, runtimeConfig?.GetInt(SettingsRegistry.AutoRefreshSeconds, 30) ?? 30);
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);

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

            int idleMinutes = Math.Max(5, runtimeConfig?.GetInt(SettingsRegistry.WorkspaceIdleMinutes, 30) ?? 30);
            registry.SweepIdle(TimeSpan.FromMinutes(idleMinutes), liveness.IsActive);

            if (DateTimeOffset.UtcNow >= nextCleanup)
            {
                nextCleanup = DateTimeOffset.UtcNow + CleanupEvery;
                int retentionDays = Math.Max(1, runtimeConfig?.GetInt(SettingsRegistry.WorkspaceRetentionDays, fallbackRetention) ?? fallbackRetention);
                try
                {
                    int deleted = registry.CleanupStaleWorkingCopies(TimeSpan.FromDays(retentionDays));
                    if (deleted > 0)
                    {
                        logger.LogInformation("Deleted {Count} stale working cop(ies) older than {Days} days.", deleted, retentionDays);
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
