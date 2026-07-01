using System.Security.Claims;
using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Identity;

/// <summary>
/// Ensures the security store exists, seeds the system roles + their permission grants, and creates the
/// bootstrap admin on first run (only when the store has no users). Idempotent — safe on every startup.
/// </summary>
public static class SecuritySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using IServiceScope scope = services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        SecurityDbContext db = sp.GetRequiredService<SecurityDbContext>();
        await db.Database.MigrateAsync(ct); // creates the schema on first run, applies pending migrations after

        RoleManager<AppRole> roleManager = sp.GetRequiredService<RoleManager<AppRole>>();
        foreach (string roleName in SecurityRoles.All)
        {
            AppRole? role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new AppRole(roleName) { IsSystem = true, Description = $"System role: {roleName}" };
                await roleManager.CreateAsync(role);
            }

            HashSet<string> granted = (await roleManager.GetClaimsAsync(role))
                .Where(c => c.Type == Permissions.ClaimType)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string permission in SecurityRoles.SystemRolePermissions[roleName])
            {
                if (granted.Add(permission))
                {
                    await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
                }
            }
        }

        UserManager<AppUser> userManager = sp.GetRequiredService<UserManager<AppUser>>();
        SecurityOptions options = sp.GetRequiredService<IOptions<SecurityOptions>>().Value;
        if (!await userManager.Users.AnyAsync(ct)
            && options.BootstrapAdmin is { UserName.Length: > 0, Password.Length: > 0 } admin)
        {
            AppUser user = new()
            {
                UserName = admin.UserName,
                Email = admin.Email,
                EmailConfirmed = true,
                DisplayName = admin.UserName,
                IsEnabled = true,
                Origin = "Local",
            };

            if ((await userManager.CreateAsync(user, admin.Password!)).Succeeded)
            {
                await userManager.AddToRoleAsync(user, SecurityRoles.Administrator);
            }
        }
    }
}
