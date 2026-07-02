namespace App.Application.Security;

/// <summary>Well-known security event names recorded to the audit trail.</summary>
public static class SecurityEvents
{
    public const string LoginSucceeded = "login.succeeded";
    public const string LoginFailed = "login.failed";
    public const string LoginLockedOut = "login.lockedout";
    public const string Logout = "logout";
    public const string UserCreated = "user.created";
    public const string UserEnabledChanged = "user.enabled_changed";
    public const string UserRolesChanged = "user.roles_changed";
    public const string UserPasswordReset = "user.password_reset";
    public const string UserUnlocked = "user.unlocked";
    public const string RoleCreated = "role.created";
    public const string RolePermissionsChanged = "role.permissions_changed";
    public const string RoleDeleted = "role.deleted";
}

/// <summary>One recorded security event.</summary>
public sealed record SecurityAuditEntry(
    string Event,
    string? UserName,
    bool Success,
    string? Detail,
    string? IpAddress,
    DateTimeOffset AtUtc);

/// <summary>
/// Append-only security audit trail (logins, lockouts, admin actions). Deployment-global, stored in the
/// security store alongside the identity data — separate from the domain change-log audit (§8).
/// </summary>
public interface ISecurityAudit
{
    Task RecordAsync(string @event, string? userName, bool success, string? detail = null, string? ipAddress = null, CancellationToken ct = default);

    Task<IReadOnlyList<SecurityAuditEntry>> QueryAsync(string? userName = null, int limit = 200, CancellationToken ct = default);

    /// <summary>Deletes events older than <paramref name="olderThan"/> (retention). Returns the count removed.</summary>
    Task<int> PruneAsync(TimeSpan olderThan, CancellationToken ct = default);
}
