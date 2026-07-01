namespace App.E2E.Tests;

/// <summary>
/// Runs App.Web with authentication required and a seeded bootstrap admin — the target for the auth-on
/// E2E flow. UserName + password are passed on the command line so the host doesn't depend on appsettings
/// being copied into the test output.
/// </summary>
public sealed class AuthWebHostFixture : WebHostFixture
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "Admin!Passw0rd1";

    protected override string ExtraArgs =>
        $"--Auth:Require=true --Auth:BootstrapAdmin:UserName={AdminUser} --Auth:BootstrapAdmin:Password={AdminPassword}";
}
