namespace App.Application.Security;

/// <summary>A configured authentication provider, as shown on the login page and the admin Providers view.</summary>
public sealed record AuthProviderInfo(string Name, string DisplayName, string Kind, bool Enabled, string? Authority);

/// <summary>
/// The authentication providers this host is configured with (local + any external OIDC). Lets the UI
/// render sign-in options and an admin overview without depending on the identity infrastructure directly.
/// </summary>
public interface IAuthProviderCatalog
{
    /// <summary>All configured providers.</summary>
    IReadOnlyList<AuthProviderInfo> Providers();

    /// <summary>Enabled external (OIDC) providers, for the login page's "Sign in with …" buttons.</summary>
    IReadOnlyList<AuthProviderInfo> ExternalSignInOptions();
}
