using App.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Identity;

/// <summary>
/// Daily security-store maintenance: prunes audit events past the retention period
/// (<c>Auth:Audit:RetentionDays</c>, default 365) so the table doesn't grow forever. Runs shortly after
/// startup, then every 24h.
/// </summary>
public sealed class SecurityMaintenanceService(
    IServiceProvider services,
    IOptions<SecurityOptions> options,
    ILogger<SecurityMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan retention = TimeSpan.FromDays(Math.Max(1, options.Value.Audit.RetentionDays));

        // Small startup delay so migrations/seeding finish first.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using IServiceScope scope = services.CreateScope();
                int removed = await scope.ServiceProvider.GetRequiredService<ISecurityAudit>().PruneAsync(retention, stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Pruned {Count} security audit event(s) older than {Days} days.", removed, retention.TotalDays);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Security audit pruning failed; will retry tomorrow.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
