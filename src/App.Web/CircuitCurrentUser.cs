using System.Security.Claims;
using App.Application.Abstractions;
using App.Application.Security;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web;

/// <summary>
/// Web-host <see cref="ICurrentUser"/>. Reads the authenticated principal from the
/// <see cref="AuthenticationStateProvider"/> — the source that works both during prerender and on the
/// live Blazor Server circuit (unlike <c>IHttpContextAccessor</c>, which is null on the circuit). When a
/// user is signed in, id / display name / roles / permissions come from their claims (expanded from
/// roles at sign-in). When no authentication is configured, it collapses to a fully-privileged guest
/// admin backed by the OS account, so local dev and the E2E suite run without credentials.
/// </summary>
internal sealed class CircuitCurrentUser(IServiceProvider services) : ICurrentUser
{
    private ClaimsPrincipal? _principal;
    private bool _resolved;

    private ClaimsPrincipal? Principal
    {
        get
        {
            if (_resolved)
            {
                return _principal;
            }

            try
            {
                AuthenticationStateProvider? provider = services.GetService<AuthenticationStateProvider>();
                // On the server provider the task is already completed (set at circuit start) — no deadlock.
                _principal = provider?.GetAuthenticationStateAsync().GetAwaiter().GetResult().User;
            }
            catch
            {
                _principal = null;
            }

            _resolved = true;
            return _principal;
        }
    }

    private bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string UserId => IsAuthenticated
        ? Principal!.FindFirst(SecurityClaims.UserId)?.Value ?? EnvironmentCurrentUser.Sanitize(Principal.Identity!.Name)
        : EnvironmentCurrentUser.Sanitize(Environment.UserName);

    public string Name => IsAuthenticated
        ? EnvironmentCurrentUser.Sanitize(Principal!.Identity!.Name)
        : EnvironmentCurrentUser.Sanitize(Environment.UserName);

    public string DisplayName => IsAuthenticated
        ? Principal!.FindFirst(SecurityClaims.DisplayName)?.Value ?? Name
        : Name;

    public IReadOnlyCollection<string> Roles => IsAuthenticated
        ? Principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()
        : [SecurityRoles.Administrator];

    // Auth off => guest admin (everything allowed); auth on => the permission claim must be present.
    public bool HasPermission(string permission) => !IsAuthenticated || Principal!.HasClaim(Permissions.ClaimType, permission);
}
