using System.Security.Claims;
using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Identity.Tests;

public sealed class ExternalSignInServiceTests
{
    // A "test" OIDC provider: JIT on, default role Reader, IdP group "admins" -> Administrator.
    private static void Oidc(Dictionary<string, string?> s, bool autoProvision = true)
    {
        s["Auth:Providers:Oidc:0:Name"] = "test";
        s["Auth:Providers:Oidc:0:AutoProvision"] = autoProvision ? "true" : "false";
        s["Auth:Providers:Oidc:0:DefaultRole"] = SecurityRoles.Reader;
        s["Auth:Providers:Oidc:0:RoleClaimType"] = "groups";
        s["Auth:Providers:Oidc:0:GroupRoleMap:admins"] = SecurityRoles.Administrator;
    }

    private static ClaimsPrincipal Principal(string? email, string? name, params string[] groups)
    {
        List<Claim> claims = [];
        if (email is not null) { claims.Add(new Claim(ClaimTypes.Email, email)); }
        if (name is not null) { claims.Add(new Claim("name", name)); }
        claims.AddRange(groups.Select(g => new Claim("groups", g)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task First_external_sign_in_provisions_a_local_user_with_the_default_role()
    {
        await using SecurityTestHost host = new(s => Oidc(s));
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            ExternalSignInService external = sp.GetRequiredService<ExternalSignInService>();
            ExternalSignInResult result = await external.ResolveAsync(
                new UserLoginInfo("test", "ext-1", "Test"), Principal("alice@x.io", "Alice"));

            Assert.NotNull(result.User);
            Assert.True(result.Provisioned);
            Assert.Equal("test", result.User!.Origin);
            Assert.Equal("alice@x.io", result.User.Email);

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            Assert.Contains(SecurityRoles.Reader, await users.GetRolesAsync(result.User));
            Assert.NotNull(await users.FindByLoginAsync("test", "ext-1")); // linked
            return true;
        });
    }

    [Fact]
    public async Task External_sign_in_links_to_an_existing_local_account_by_email()
    {
        await using SecurityTestHost host = new(s => Oidc(s));
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            Assert.True((await directory.CreateUserAsync(new CreateUserRequest("bob", "bob@x.io", "Bob", "Bob!Passw0rd12", [SecurityRoles.Editor]))).Succeeded);

            ExternalSignInService external = sp.GetRequiredService<ExternalSignInService>();
            ExternalSignInResult result = await external.ResolveAsync(
                new UserLoginInfo("test", "ext-bob", "Test"), Principal("bob@x.io", "Bob External"));

            Assert.NotNull(result.User);
            Assert.False(result.Provisioned);        // linked, not created
            Assert.Equal("bob", result.User!.UserName);

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            Assert.Contains(SecurityRoles.Editor, await users.GetRolesAsync(result.User)); // local role kept
            Assert.NotNull(await users.FindByLoginAsync("test", "ext-bob"));
            return true;
        });
    }

    [Fact]
    public async Task Idp_group_claims_grant_the_mapped_role_additively()
    {
        await using SecurityTestHost host = new(s => Oidc(s));
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            ExternalSignInService external = sp.GetRequiredService<ExternalSignInService>();
            ExternalSignInResult result = await external.ResolveAsync(
                new UserLoginInfo("test", "ext-carol", "Test"), Principal("carol@x.io", "Carol", "admins", "other"));

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            IList<string> roles = await users.GetRolesAsync(result.User!);
            Assert.Contains(SecurityRoles.Administrator, roles); // mapped from the "admins" group
            Assert.Contains(SecurityRoles.Reader, roles);        // default role also kept
            return true;
        });
    }

    [Fact]
    public async Task Provisioning_off_rejects_an_unknown_external_user()
    {
        await using SecurityTestHost host = new(s => Oidc(s, autoProvision: false));
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            ExternalSignInService external = sp.GetRequiredService<ExternalSignInService>();
            ExternalSignInResult result = await external.ResolveAsync(
                new UserLoginInfo("test", "ext-dave", "Test"), Principal("dave@x.io", "Dave"));

            Assert.Null(result.User);
            Assert.NotNull(result.Error);
            return true;
        });
    }

    [Fact]
    public async Task Provider_catalog_lists_local_and_configured_oidc_providers()
    {
        await using SecurityTestHost host = new(s => Oidc(s));
        await host.SeedAsync();

        await host.InScopeAsync(sp =>
        {
            IAuthProviderCatalog catalog = sp.GetRequiredService<IAuthProviderCatalog>();
            IReadOnlyList<AuthProviderInfo> all = catalog.Providers();
            Assert.Contains(all, p => p is { Kind: "Local" });
            Assert.Contains(all, p => p.Name == "test" && p.Kind == "OIDC");
            return Task.FromResult(true);
        });
    }
}
