using System.Security.Claims;
using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Identity;

/// <summary>Outcome of resolving an external (OIDC) sign-in to a local account.</summary>
public sealed record ExternalSignInResult(AppUser? User, string? Error, bool Provisioned);

/// <summary>
/// Turns a validated external login (from an OIDC provider) into a local <see cref="AppUser"/>:
/// reuse an existing link, else link by verified email, else just-in-time provision (when the provider
/// allows it) with its default role — then additively grant any roles mapped from the IdP's group/role
/// claims. Pure application logic over Identity, so it is unit-testable without a live IdP.
/// </summary>
public sealed class ExternalSignInService(
    UserManager<AppUser> users,
    RoleManager<AppRole> roles,
    IOptions<SecurityOptions> options)
{
    public async Task<ExternalSignInResult> ResolveAsync(UserLoginInfo login, ClaimsPrincipal principal)
    {
        OidcProviderOptions? config = options.Value.Providers.Oidc
            .FirstOrDefault(p => string.Equals(p.Name, login.LoginProvider, StringComparison.OrdinalIgnoreCase));

        string? email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");
        string? name = principal.FindFirstValue("name") ?? principal.FindFirstValue(ClaimTypes.Name);

        bool provisioned = false;
        AppUser? user = await users.FindByLoginAsync(login.LoginProvider, login.ProviderKey);

        // Link to an existing local account with the same (verified) email.
        if (user is null && !string.IsNullOrWhiteSpace(email))
        {
            user = await users.FindByEmailAsync(email);
            if (user is not null)
            {
                await users.AddLoginAsync(user, login);
            }
        }

        // Just-in-time provisioning.
        if (user is null)
        {
            if (config is null || !config.AutoProvision)
            {
                return new ExternalSignInResult(null, "No account is linked to this sign-in, and automatic provisioning is off.", false);
            }

            user = new AppUser
            {
                UserName = email ?? $"{login.LoginProvider}:{login.ProviderKey}",
                Email = email,
                EmailConfirmed = !string.IsNullOrWhiteSpace(email),
                DisplayName = name ?? email,
                Origin = login.LoginProvider,
                IsEnabled = true,
            };

            IdentityResult created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                return new ExternalSignInResult(null, string.Join(" ", created.Errors.Select(e => e.Description)), false);
            }

            await users.AddLoginAsync(user, login);
            provisioned = true;
            if (!string.IsNullOrWhiteSpace(config.DefaultRole) && await roles.RoleExistsAsync(config.DefaultRole))
            {
                await users.AddToRoleAsync(user, config.DefaultRole);
            }
        }

        if (!user.IsEnabled)
        {
            return new ExternalSignInResult(null, "This account is disabled.", false);
        }

        await SyncMappedRolesAsync(user, principal, config);

        user.LastLoginUtc = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);
        return new ExternalSignInResult(user, null, provisioned);
    }

    // Grant (additively — never strip locally-assigned roles) any app roles mapped from the IdP's groups.
    private async Task SyncMappedRolesAsync(AppUser user, ClaimsPrincipal principal, OidcProviderOptions? config)
    {
        if (config?.RoleClaimType is not { Length: > 0 } claimType || config.GroupRoleMap.Count == 0)
        {
            return;
        }

        IList<string> current = await users.GetRolesAsync(user);
        foreach (Claim claim in principal.FindAll(claimType))
        {
            if (config.GroupRoleMap.TryGetValue(claim.Value, out string? role)
                && !current.Contains(role)
                && await roles.RoleExistsAsync(role))
            {
                await users.AddToRoleAsync(user, role);
                current.Add(role);
            }
        }
    }
}
