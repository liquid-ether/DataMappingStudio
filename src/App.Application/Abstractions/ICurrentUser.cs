using App.Application.Security;

namespace App.Application.Abstractions;

/// <summary>
/// The user performing the current action — recorded as <c>changed_by</c> on every change-log entry so
/// the audit trail and conflict attribution reflect the real analyst, not a placeholder, and used to key
/// each user's per-user workspace. Resolved per action (scoped): on the desktop it is the Windows account
/// (a fully-privileged single user); on the web host it is the authenticated user with their roles and
/// permissions (falling back to the OS account when no authentication is configured).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Stable, immutable id that keys the workspace and attributes changes (survives renames).</summary>
    string UserId { get; }

    /// <summary>The login / account name.</summary>
    string Name { get; }

    /// <summary>A friendly name for display (defaults to <see cref="Name"/>).</summary>
    string DisplayName { get; }

    /// <summary>The roles the user holds.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Whether the user is granted the given <see cref="Permissions"/> operation.</summary>
    bool HasPermission(string permission);
}

/// <summary>
/// Default <see cref="ICurrentUser"/> backed by the OS account (<c>Environment.UserName</c>) — correct
/// for the single-user desktop app and a sensible fallback elsewhere (dev / E2E). This user is a
/// fully-privileged Administrator: the desktop is single-user and owns everything, and with no
/// authentication configured the web host collapses to this guest admin. Hosts with real authentication
/// register their own implementation.
/// </summary>
public sealed class EnvironmentCurrentUser : ICurrentUser
{
    public string UserId => Name;

    public string Name { get; } = Sanitize(Environment.UserName);

    public string DisplayName => Name;

    public IReadOnlyCollection<string> Roles { get; } = [SecurityRoles.Administrator];

    public bool HasPermission(string permission) => true;

    public static string Sanitize(string? name)
        => string.IsNullOrWhiteSpace(name) ? "unknown" : name.Trim();
}
