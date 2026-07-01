using System.Security.Claims;
using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Identity;

/// <summary>
/// Expands the signed-in user's roles into <see cref="Permissions.ClaimType"/> claims on the principal
/// (from each role's role-claims), and adds stable <c>uid</c> + <c>display_name</c> claims. Authorization
/// policies then require the permission claim, and the UI gates on it via <c>ICurrentUser.HasPermission</c>.
/// </summary>
public sealed class PermissionClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentityOptions> options,
    IOptions<SecurityOptions> securityOptions)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(userManager, roleManager, options)
{
    public override async Task<ClaimsPrincipal> CreateAsync(AppUser user)
    {
        ClaimsPrincipal principal = await base.CreateAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;

        identity.AddClaim(new Claim(SecurityClaims.UserId, user.Id.ToString()));
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(SecurityClaims.DisplayName, user.DisplayName));
        }

        HashSet<string> permissions = new(StringComparer.Ordinal);
        foreach (string roleName in await UserManager.GetRolesAsync(user))
        {
            AppRole? role = await RoleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            foreach (Claim claim in await RoleManager.GetClaimsAsync(role))
            {
                if (claim.Type == Permissions.ClaimType)
                {
                    permissions.Add(claim.Value);
                }
            }
        }

        foreach (string permission in permissions)
        {
            identity.AddClaim(new Claim(Permissions.ClaimType, permission));
        }

        // Per-role MFA enforcement: mark the session pending when policy requires two-factor for this
        // user and they haven't enrolled — the host then restricts them to the account pages.
        string require = securityOptions.Value.Providers.Local.Mfa.Require;
        bool mfaRequired = string.Equals(require, "All", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(require, "Administrators", StringComparison.OrdinalIgnoreCase)
                && principal.IsInRole(SecurityRoles.Administrator));
        if (mfaRequired && !await UserManager.GetTwoFactorEnabledAsync(user))
        {
            identity.AddClaim(new Claim(SecurityClaims.MfaPending, "1"));
        }

        return principal;
    }
}
