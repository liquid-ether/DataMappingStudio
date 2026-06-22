using App.Application.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Remote;

/// <summary>
/// Background auto-refresh (Architecture §9): on an interval, fast-forwards untouched cells from the
/// remote fold into the local working copy via <see cref="ISyncCoordinator.Refresh"/>. Non-disruptive —
/// it never clobbers unpublished local edits (the planner flags those for review instead).
/// </summary>
public sealed class AutoRefreshService(ISyncCoordinator coordinator, ILogger<AutoRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
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
