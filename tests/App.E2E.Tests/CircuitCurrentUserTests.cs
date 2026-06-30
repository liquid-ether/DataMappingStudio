using System.Security.Claims;
using App.Application.Abstractions;
using App.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace App.E2E.Tests;

/// <summary>
/// The web host's <see cref="ICurrentUser"/> for the authenticated (multi-user) path: it must read the
/// signed-in Windows identity from the <see cref="AuthenticationStateProvider"/> (the circuit-safe source)
/// so each user keys their own workspace, and fall back to the OS account when auth is off.
/// </summary>
public sealed class CircuitCurrentUserTests
{
    private sealed class StubAuthProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private static IServiceProvider Services(AuthenticationStateProvider? provider)
    {
        ServiceCollection services = new();
        if (provider is not null)
        {
            services.AddSingleton(provider);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Uses_the_authenticated_windows_identity()
    {
        ClaimsPrincipal alice = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, @"CONTOSO\alice")], authenticationType: "Negotiate"));

        ICurrentUser user = new CircuitCurrentUser(Services(new StubAuthProvider(alice)));

        // The raw login is the change author; the registry sanitizes it to a key (CONTOSO_alice).
        Assert.Equal(@"CONTOSO\alice", user.Name);
    }

    [Fact]
    public void Falls_back_to_the_os_account_for_an_anonymous_principal()
    {
        ClaimsPrincipal anonymous = new(new ClaimsIdentity()); // not authenticated

        ICurrentUser user = new CircuitCurrentUser(Services(new StubAuthProvider(anonymous)));

        Assert.Equal(EnvironmentCurrentUser.Sanitize(Environment.UserName), user.Name);
    }

    [Fact]
    public void Falls_back_when_no_authentication_is_configured()
    {
        ICurrentUser user = new CircuitCurrentUser(Services(provider: null));

        Assert.Equal(EnvironmentCurrentUser.Sanitize(Environment.UserName), user.Name);
    }
}
