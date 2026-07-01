using Microsoft.AspNetCore.Identity;

namespace App.Infrastructure.Identity;

/// <summary>A user in the security store. GUID-keyed so the id is a stable workspace/audit key.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }

    /// <summary>Admin can disable a user without deleting them; disabled users cannot sign in.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>How the account was created: "Local" or an external provider name.</summary>
    public string Origin { get; set; } = "Local";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginUtc { get; set; }
}

/// <summary>A role. Permissions are stored as role claims; system roles cannot be deleted.</summary>
public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole() { }

    public AppRole(string name) : base(name) { }

    public string? Description { get; set; }

    public bool IsSystem { get; set; }
}

/// <summary>One recorded security event (login, lockout, admin action).</summary>
public sealed class SecurityAuditEvent
{
    public long Id { get; set; }

    public string Event { get; set; } = string.Empty;

    public string? UserName { get; set; }

    public bool Success { get; set; }

    public string? Detail { get; set; }

    public string? IpAddress { get; set; }

    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
}
