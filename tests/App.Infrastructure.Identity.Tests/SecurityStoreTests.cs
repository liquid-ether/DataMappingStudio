using System.Security.Claims;
using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Identity.Tests;

public sealed class SecurityStoreTests
{
    [Fact]
    public async Task Seeding_creates_system_roles_with_their_grants_and_the_bootstrap_admin()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            RoleManager<AppRole> roles = sp.GetRequiredService<RoleManager<AppRole>>();
            foreach (string roleName in SecurityRoles.All)
            {
                AppRole role = (await roles.FindByNameAsync(roleName))!;
                Assert.True(role.IsSystem);
                IReadOnlyList<string> perms = (await roles.GetClaimsAsync(role))
                    .Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList();
                Assert.Equal(SecurityRoles.SystemRolePermissions[roleName].OrderBy(p => p), perms.OrderBy(p => p));
            }

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            AppUser admin = (await users.FindByNameAsync("admin"))!;
            Assert.Contains(SecurityRoles.Administrator, await users.GetRolesAsync(admin));
            Assert.True(await users.CheckPasswordAsync(admin, SecurityTestHost.AdminPassword));
            return true;
        });
    }

    [Fact]
    public async Task Sign_in_principal_carries_the_permission_claims_expanded_from_roles()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            IUserClaimsPrincipalFactory<AppUser> factory = sp.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
            AppUser admin = (await users.FindByNameAsync("admin"))!;

            ClaimsPrincipal principal = await factory.CreateAsync(admin);

            Assert.True(principal.HasClaim(Permissions.ClaimType, Permissions.DataPublish));
            Assert.True(principal.HasClaim(Permissions.ClaimType, Permissions.UsersManage));
            Assert.NotNull(principal.FindFirst(SecurityClaims.UserId));
            return true;
        });
    }

    [Fact]
    public async Task Reader_is_granted_only_view_permissions()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            OperationResult created = await directory.CreateUserAsync(
                new CreateUserRequest("reader", null, "Reader One", "Reader!Passw0rd1", [SecurityRoles.Reader]));
            Assert.True(created.Succeeded, string.Join(";", created.Errors));

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            IUserClaimsPrincipalFactory<AppUser> factory = sp.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
            ClaimsPrincipal principal = await factory.CreateAsync((await users.FindByNameAsync("reader"))!);

            Assert.True(principal.HasClaim(Permissions.ClaimType, Permissions.DataView));
            Assert.False(principal.HasClaim(Permissions.ClaimType, Permissions.DataPublish));
            Assert.False(principal.HasClaim(Permissions.ClaimType, Permissions.UsersManage));
            return true;
        });
    }

    [Fact]
    public async Task User_directory_supports_create_roles_enable_and_reset()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            Assert.True((await directory.CreateUserAsync(new CreateUserRequest("bob", "bob@x.io", "Bob", "Bob!Passw0rd12", [SecurityRoles.Editor]))).Succeeded);

            string bobId = (await directory.ListUsersAsync("bob")).Single().Id;

            Assert.True((await directory.SetRolesAsync(bobId, [SecurityRoles.Publisher])).Succeeded);
            Assert.Equal([SecurityRoles.Publisher], (await directory.ListUsersAsync("bob")).Single().Roles);

            Assert.True((await directory.SetEnabledAsync(bobId, false)).Succeeded);
            Assert.False((await directory.ListUsersAsync("bob")).Single().IsEnabled);

            Assert.True((await directory.ResetPasswordAsync(bobId, "Bob!Newpass0rd12")).Succeeded);

            // Custom role with a chosen permission set.
            Assert.True((await directory.CreateRoleAsync("Auditor", "read + history", [Permissions.HistoryView])).Succeeded);
            RoleSummary auditor = (await directory.ListRolesAsync()).Single(r => r.Name == "Auditor");
            Assert.False(auditor.IsSystem);
            Assert.Equal([Permissions.HistoryView], auditor.Permissions);

            Assert.True((await directory.DeleteRoleAsync("Auditor")).Succeeded);
            return true;
        });
    }

    [Fact]
    public async Task System_roles_cannot_be_deleted_and_the_last_admin_cannot_be_demoted()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();

            Assert.False((await directory.DeleteRoleAsync(SecurityRoles.Administrator)).Succeeded);

            string adminId = (await directory.ListUsersAsync("admin")).Single().Id;
            OperationResult demote = await directory.SetRolesAsync(adminId, [SecurityRoles.Reader]);
            Assert.False(demote.Succeeded); // last administrator is protected
            return true;
        });
    }

    [Fact]
    public async Task Audit_pruning_removes_only_events_past_the_retention_period()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            SecurityDbContext db = sp.GetRequiredService<SecurityDbContext>();
            db.SecurityAuditEvents.Add(new SecurityAuditEvent { Event = "old", Success = true, AtUtc = DateTimeOffset.UtcNow.AddDays(-400) });
            db.SecurityAuditEvents.Add(new SecurityAuditEvent { Event = "recent", Success = true, AtUtc = DateTimeOffset.UtcNow.AddDays(-10) });
            await db.SaveChangesAsync();

            ISecurityAudit audit = sp.GetRequiredService<ISecurityAudit>();
            int removed = await audit.PruneAsync(TimeSpan.FromDays(365));

            Assert.Equal(1, removed);
            IReadOnlyList<SecurityAuditEntry> remaining = await audit.QueryAsync();
            Assert.Single(remaining);
            Assert.Equal("recent", remaining[0].Event);
            return true;
        });
    }

    [Fact]
    public async Task Security_audit_records_and_queries_events()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            ISecurityAudit audit = sp.GetRequiredService<ISecurityAudit>();
            await audit.RecordAsync(SecurityEvents.LoginFailed, "mallory", false, "bad password", "10.0.0.1");
            await audit.RecordAsync(SecurityEvents.LoginSucceeded, "admin", true);

            IReadOnlyList<SecurityAuditEntry> all = await audit.QueryAsync();
            Assert.Equal(2, all.Count);
            Assert.Equal(SecurityEvents.LoginSucceeded, all[0].Event); // newest first

            IReadOnlyList<SecurityAuditEntry> mallory = await audit.QueryAsync("mallory");
            Assert.Single(mallory);
            Assert.False(mallory[0].Success);
            return true;
        });
    }
}
