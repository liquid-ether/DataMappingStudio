using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Identity.Tests;

public sealed class SessionRevocationTests
{
    [Fact]
    public async Task Revoking_sessions_rotates_the_security_stamp_so_existing_cookies_are_invalidated()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            await directory.CreateUserAsync(new CreateUserRequest("hank", "hank@x.io", "Hank", "Hank!Passw0rd12", [SecurityRoles.Reader]));
            string id = (await directory.ListUsersAsync("hank")).Single().Id;

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            string before = await users.GetSecurityStampAsync((await users.FindByIdAsync(id))!);

            Assert.True((await directory.RevokeSessionsAsync(id)).Succeeded);

            string after = await users.GetSecurityStampAsync((await users.FindByIdAsync(id))!);
            Assert.NotEqual(before, after); // the stamp changed → prior auth cookies no longer validate
            return true;
        });
    }
}
