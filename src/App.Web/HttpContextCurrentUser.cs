using App.Application.Abstractions;

namespace App.Web;

/// <summary>
/// Web-host <see cref="ICurrentUser"/>: the authenticated request user (e.g. the Windows account behind
/// Negotiate), falling back to the OS account when no authentication is configured (development). Scoped,
/// so each Blazor circuit / request records its own user as <c>changed_by</c>.
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Name => EnvironmentCurrentUser.Sanitize(
        accessor.HttpContext?.User.Identity?.Name ?? Environment.UserName);
}
