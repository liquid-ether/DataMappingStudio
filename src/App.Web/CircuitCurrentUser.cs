using App.Application.Abstractions;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web;

/// <summary>
/// Web-host <see cref="ICurrentUser"/>. Reads the authenticated principal from the
/// <see cref="AuthenticationStateProvider"/> — the source that works both during prerender and on the
/// live Blazor Server circuit (unlike <c>IHttpContextAccessor</c>, which is null on the circuit). Falls
/// back to the OS account when no authentication is configured (development / E2E), which collapses to a
/// single shared "dev" workspace. The name keys the per-user workspace and is recorded as change author.
/// </summary>
internal sealed class CircuitCurrentUser(IServiceProvider services) : ICurrentUser
{
    public string Name
    {
        get
        {
            AuthenticationStateProvider? provider = services.GetService<AuthenticationStateProvider>();
            if (provider is not null)
            {
                try
                {
                    // On the server provider this task is already completed (set at circuit start), so
                    // there is no blocking/deadlock risk.
                    string? name = provider.GetAuthenticationStateAsync().GetAwaiter().GetResult().User.Identity?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return EnvironmentCurrentUser.Sanitize(name);
                    }
                }
                catch
                {
                    // Fall through to the OS-account default.
                }
            }

            return EnvironmentCurrentUser.Sanitize(Environment.UserName);
        }
    }
}
