using System.Security.Claims;
using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

/// <summary>Admin user + role management over ASP.NET Core Identity.</summary>
public sealed class IdentityUserDirectory(
    UserManager<AppUser> users,
    RoleManager<AppRole> roles) : IUserDirectory
{
    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(string? search = null, CancellationToken ct = default)
    {
        IQueryable<AppUser> query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            query = query.Where(u => u.UserName!.Contains(s) || (u.DisplayName != null && u.DisplayName.Contains(s)) || (u.Email != null && u.Email.Contains(s)));
        }

        List<AppUser> list = await query.OrderBy(u => u.UserName).Take(500).ToListAsync(ct);
        List<UserSummary> result = new(list.Count);
        foreach (AppUser u in list)
        {
            IList<string> userRoles = await users.GetRolesAsync(u);
            bool lockedOut = u.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;
            result.Add(new UserSummary(u.Id.ToString(), u.UserName ?? "", u.DisplayName, u.Email, u.IsEnabled, lockedOut, [.. userRoles], u.LastLoginUtc, u.Origin));
        }

        return result;
    }

    public async Task<OperationResult> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        AppUser user = new()
        {
            UserName = request.UserName,
            Email = request.Email,
            EmailConfirmed = true, // admin-created
            DisplayName = request.DisplayName,
            IsEnabled = true,
            Origin = "Local",
        };

        IdentityResult created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return ToResult(created);
        }

        IReadOnlyList<string> valid = request.Roles.Where(SecurityRolesExist).ToList();
        if (valid.Count > 0)
        {
            IdentityResult roleResult = await users.AddToRolesAsync(user, valid);
            if (!roleResult.Succeeded)
            {
                return ToResult(roleResult);
            }
        }

        return OperationResult.Ok;

        bool SecurityRolesExist(string r) => roles.RoleExistsAsync(r).GetAwaiter().GetResult();
    }

    public async Task<OperationResult> SetEnabledAsync(string userId, bool enabled, CancellationToken ct = default)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        user.IsEnabled = enabled;
        return ToResult(await users.UpdateAsync(user));
    }

    public async Task<OperationResult> SetRolesAsync(string userId, IReadOnlyList<string> roleNames, CancellationToken ct = default)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        IList<string> current = await users.GetRolesAsync(user);
        IReadOnlyList<string> target = roleNames.Where(r => roles.RoleExistsAsync(r).GetAwaiter().GetResult()).Distinct(StringComparer.Ordinal).ToList();

        // Guard: never strip the last Administrator (avoids locking everyone out).
        if (current.Contains(SecurityRoles.Administrator) && !target.Contains(SecurityRoles.Administrator)
            && (await users.GetUsersInRoleAsync(SecurityRoles.Administrator)).Count <= 1)
        {
            return OperationResult.Fail("Cannot remove the last administrator.");
        }

        IdentityResult removed = await users.RemoveFromRolesAsync(user, current.Except(target, StringComparer.Ordinal));
        if (!removed.Succeeded)
        {
            return ToResult(removed);
        }

        return ToResult(await users.AddToRolesAsync(user, target.Except(current, StringComparer.Ordinal)));
    }

    public async Task<OperationResult> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        string token = await users.GeneratePasswordResetTokenAsync(user);
        return ToResult(await users.ResetPasswordAsync(user, token, newPassword));
    }

    public async Task<OperationResult> UnlockAsync(string userId, CancellationToken ct = default)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        await users.SetLockoutEndDateAsync(user, null);
        return ToResult(await users.ResetAccessFailedCountAsync(user));
    }

    public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken ct = default)
    {
        List<AppRole> list = await roles.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        List<RoleSummary> result = new(list.Count);
        foreach (AppRole role in list)
        {
            IList<Claim> claims = await roles.GetClaimsAsync(role);
            IReadOnlyList<string> perms = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList();
            int members = (await users.GetUsersInRoleAsync(role.Name!)).Count;
            result.Add(new RoleSummary(role.Name ?? "", role.Description, role.IsSystem, perms, members));
        }

        return result;
    }

    public async Task<OperationResult> CreateRoleAsync(string name, string? description, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        if (await roles.RoleExistsAsync(name))
        {
            return OperationResult.Fail("A role with that name already exists.");
        }

        AppRole role = new(name) { Description = description, IsSystem = false };
        IdentityResult created = await roles.CreateAsync(role);
        return created.Succeeded ? await SetRolePermissionsAsync(role, permissions) : ToResult(created);
    }

    public async Task<OperationResult> UpdateRolePermissionsAsync(string name, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        AppRole? role = await roles.FindByNameAsync(name);
        return role is null ? OperationResult.Fail("Role not found.") : await SetRolePermissionsAsync(role, permissions);
    }

    public async Task<OperationResult> DeleteRoleAsync(string name, CancellationToken ct = default)
    {
        AppRole? role = await roles.FindByNameAsync(name);
        if (role is null)
        {
            return OperationResult.Fail("Role not found.");
        }

        if (role.IsSystem)
        {
            return OperationResult.Fail("System roles cannot be deleted.");
        }

        return ToResult(await roles.DeleteAsync(role));
    }

    private async Task<OperationResult> SetRolePermissionsAsync(AppRole role, IReadOnlyList<string> permissions)
    {
        IReadOnlyList<string> valid = permissions.Where(p => Permissions.All.Contains(p)).Distinct(StringComparer.Ordinal).ToList();
        IList<Claim> existing = await roles.GetClaimsAsync(role);
        foreach (Claim claim in existing.Where(c => c.Type == Permissions.ClaimType))
        {
            await roles.RemoveClaimAsync(role, claim);
        }

        foreach (string permission in valid)
        {
            await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
        }

        return OperationResult.Ok;
    }

    private static OperationResult ToResult(IdentityResult result)
        => result.Succeeded ? OperationResult.Ok : OperationResult.Fail(result.Errors.Select(e => e.Description).ToArray());
}
