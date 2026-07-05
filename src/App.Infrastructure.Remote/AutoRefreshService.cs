using App.Application.Abstractions;
using App.Application.Configuration;
using App.Application.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Remote;

/// <summary>
/// Background auto-refresh (Architecture §9): on an interval, fast-forwards untouched cells from the
/// remote fold into the local working copy via <see cref="ISyncCoordinator.Refresh"/>. Non-disruptive —
/// it never clobbers unpublished local edits (the planner flags those for review instead). The interval
/// is the live runtime setting <c>Sync.AutoRefreshSeconds</c> when a runtime config is wired, read per
/// tick so changes apply without a restart.
/// </summary>
public sealed class AutoRefreshService(ISyncCoordinator coordinator, ILogger<AutoRefreshService> logger, IRuntimeConfig? config = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int seconds = Math.Max(5, config?.GetInt(SettingsRegistry.AutoRefreshSeconds, 30) ?? 30);
            await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
            try
            {
                RefreshResult result = coordinator.Refresh();
                if (result.Applied > 0 || result.Flagged > 0)
                {
                    logger.LogInformation("Auto-refresh: {Applied} applied, {Flagged} flagged for review.", result.Applied, result.Flagged);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Auto-refresh tick failed; will retry next interval.");
            }
        }
    }
}
