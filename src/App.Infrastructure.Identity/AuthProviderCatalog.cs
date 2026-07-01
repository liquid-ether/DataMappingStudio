using App.Application.Security;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Identity;

/// <summary>Exposes the configured providers (local + OIDC) to the UI.</summary>
public sealed class AuthProviderCatalog(IOptions<SecurityOptions> options) : IAuthProviderCatalog
{
    public IReadOnlyList<AuthProviderInfo> Providers()
    {
        SecurityOptions o = options.Value;
        List<AuthProviderInfo> list = [new("Local", "Local (username & password)", "Local", o.Providers.Local.Enabled, null)];
        list.AddRange(o.Providers.Oidc.Select(p =>
            new AuthProviderInfo(p.Name, p.DisplayName ?? p.Name, "OIDC", p.Enabled, p.Authority)));
        return list;
    }

    public IReadOnlyList<AuthProviderInfo> ExternalSignInOptions()
        => options.Value.Providers.Oidc
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Authority) && !string.IsNullOrWhiteSpace(p.ClientId))
            .Select(p => new AuthProviderInfo(p.Name, p.DisplayName ?? p.Name, "OIDC", true, p.Authority))
            .ToList();
}
