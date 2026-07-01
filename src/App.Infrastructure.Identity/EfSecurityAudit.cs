using App.Application.Security;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

/// <summary>Writes and queries the security audit trail in the security store.</summary>
public sealed class EfSecurityAudit(SecurityDbContext db) : ISecurityAudit
{
    public async Task RecordAsync(string @event, string? userName, bool success, string? detail = null, string? ipAddress = null, CancellationToken ct = default)
    {
        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            Event = @event,
            UserName = userName,
            Success = success,
            Detail = detail,
            IpAddress = ipAddress,
            AtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SecurityAuditEntry>> QueryAsync(string? userName = null, int limit = 200, CancellationToken ct = default)
    {
        IQueryable<SecurityAuditEvent> query = db.SecurityAuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(userName))
        {
            query = query.Where(e => e.UserName == userName);
        }

        // Order by the autoincrement Id (monotonic with insertion = chronological) rather than AtUtc:
        // it is deterministic, and SQLite can't ORDER BY a DateTimeOffset (SQL Server can — Id works on both).
        List<SecurityAuditEvent> rows = await query
            .OrderByDescending(e => e.Id)
            .Take(Math.Clamp(limit, 1, 2000))
            .ToListAsync(ct);

        return rows.Select(e => new SecurityAuditEntry(e.Event, e.UserName, e.Success, e.Detail, e.IpAddress, e.AtUtc)).ToList();
    }
}
