namespace App.Application.Abstractions;

/// <summary>
/// The user performing the current action — recorded as <c>changed_by</c> on every change-log entry so
/// the audit trail and conflict attribution reflect the real analyst, not a placeholder. Resolved per
/// action (scoped): on the desktop it is the Windows account; on the web host it is the authenticated
/// user (falling back to the OS account when no authentication is configured).
/// </summary>
public interface ICurrentUser
{
    /// <summary>A short, stable identifier for the acting user (e.g. their login name).</summary>
    string Name { get; }
}

/// <summary>
/// Default <see cref="ICurrentUser"/> backed by the OS account (<c>Environment.UserName</c>) — correct
/// for the single-user desktop app and a sensible fallback elsewhere. Hosts with real authentication
/// register their own implementation.
/// </summary>
public sealed class EnvironmentCurrentUser : ICurrentUser
{
    public string Name { get; } = Sanitize(Environment.UserName);

    public static string Sanitize(string? name)
        => string.IsNullOrWhiteSpace(name) ? "unknown" : name.Trim();
}
