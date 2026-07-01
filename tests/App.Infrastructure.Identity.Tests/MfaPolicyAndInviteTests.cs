using System.Security.Claims;
using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Identity.Tests;

public sealed class MfaPolicyAndInviteTests
{
    [Fact]
    public async Task Mfa_policy_marks_only_the_targeted_roles_as_pending_until_they_enrol()
    {
        await using SecurityTestHost host = new(s => s["Auth:Providers:Local:Mfa:Require"] = "Administrators");
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            await directory.CreateUserAsync(new CreateUserRequest("ivy", "ivy@x.io", "Ivy", "Ivy!Passw0rd123", [SecurityRoles.Reader]));

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            IUserClaimsPrincipalFactory<AppUser> factory = sp.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();

            // The admin (policy target) without 2FA is marked pending; the reader is not.
            ClaimsPrincipal admin = await factory.CreateAsync((await users.FindByNameAsync("admin"))!);
            ClaimsPrincipal reader = await factory.CreateAsync((await users.FindByNameAsync("ivy"))!);
            Assert.True(admin.HasClaim(c => c.Type == SecurityClaims.MfaPending));
            Assert.False(reader.HasClaim(c => c.Type == SecurityClaims.MfaPending));

            // Once the admin enrols, the marker is gone.
            string adminId = (await users.FindByNameAsync("admin"))!.Id.ToString();
            AccountService accounts = sp.GetRequiredService<AccountService>();
            await accounts.BeginMfaEnrollmentAsync(adminId);
            await users.SetTwoFactorEnabledAsync((await users.FindByIdAsync(adminId))!, true);
            ClaimsPrincipal enrolled = await factory.CreateAsync((await users.FindByIdAsync(adminId))!);
            Assert.False(enrolled.HasClaim(c => c.Type == SecurityClaims.MfaPending));
            return true;
        });
    }

    [Fact]
    public async Task Mfa_policy_none_marks_nobody()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            IUserClaimsPrincipalFactory<AppUser> factory = sp.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
            ClaimsPrincipal admin = await factory.CreateAsync((await users.FindByNameAsync("admin"))!);
            Assert.False(admin.HasClaim(c => c.Type == SecurityClaims.MfaPending));
            return true;
        });
    }

    [Fact]
    public async Task Invite_creates_a_passwordless_user_whose_token_sets_the_first_password()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            InviteResult invite = await directory.InviteUserAsync("jack", "jack@x.io", [SecurityRoles.Editor]);
            Assert.True(invite.Result.Succeeded, string.Join(";", invite.Result.Errors));

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            AppUser jack = (await users.FindByIdAsync(invite.UserId!))!;
            Assert.False(await users.HasPasswordAsync(jack));               // passwordless until the link is used
            Assert.Contains(SecurityRoles.Editor, await users.GetRolesAsync(jack));

            // The invitee follows the link (= the reset flow) and sets their first password.
            AccountService accounts = sp.GetRequiredService<AccountService>();
            Assert.True((await accounts.ResetPasswordAsync(invite.UserId!, invite.Token!, "Jack!Passw0rd123")).Succeeded);
            Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(invite.UserId!))!, "Jack!Passw0rd123"));
            return true;
        });
    }
}
