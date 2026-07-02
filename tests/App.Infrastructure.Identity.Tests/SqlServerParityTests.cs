using App.Application.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Identity.Tests;

/// <summary>
/// Store-provider parity: the same seeding + user/role flows that run on SQLite must work on SQL Server
/// (the multi-host provider), including its own migrations assembly. Uses SQL Server LocalDB and skips
/// when it isn't installed, so the suite stays green on machines without it (CI's windows runner has it).
/// </summary>
public sealed class SqlServerParityTests
{
    private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=5";

    private static bool LocalDbAvailable()
    {
        try
        {
            using SqlConnection connection = new(LocalDb);
            connection.Open();
            return true;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [SkippableFact]
    public async Task Seeding_and_user_role_flows_work_on_sql_server()
    {
        Skip.IfNot(LocalDbAvailable(), "SQL Server LocalDB is not available on this machine.");

        string database = "dms_sec_parity_" + Guid.NewGuid().ToString("N");
        await using SecurityTestHost host = new(s =>
        {
            s["Auth:Store:Provider"] = "SqlServer";
            s["Auth:Store:ConnectionString"] = $@"Server=(localdb)\MSSQLLocalDB;Database={database};Integrated Security=true";
        });

        try
        {
            await host.SeedAsync(); // applies the SQL Server migrations assembly

            await host.InScopeAsync(async sp =>
            {
                IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();

                // Same flows the SQLite tests cover: seeded roles, create, role change, enable toggle, audit.
                IReadOnlyList<RoleSummary> roles = await directory.ListRolesAsync();
                Assert.Equal(SecurityRoles.All.OrderBy(r => r), roles.Select(r => r.Name).OrderBy(r => r));
                Assert.All(roles, r => Assert.True(r.IsSystem));

                OperationResult created = await directory.CreateUserAsync(
                    new CreateUserRequest("parity", "parity@x.io", "Parity", "Parity!Passw0rd1", [SecurityRoles.Editor]));
                Assert.True(created.Succeeded, string.Join(";", created.Errors));

                UserSummary user = (await directory.ListUsersAsync("parity")).Single();
                Assert.True((await directory.SetRolesAsync(user.Id, [SecurityRoles.Publisher])).Succeeded);
                Assert.True((await directory.SetEnabledAsync(user.Id, false)).Succeeded);
                Assert.False((await directory.ListUsersAsync("parity")).Single().IsEnabled);

                ISecurityAudit audit = sp.GetRequiredService<ISecurityAudit>();
                await audit.RecordAsync(SecurityEvents.LoginSucceeded, "parity", true);
                Assert.Single(await audit.QueryAsync("parity"));
                Assert.Equal(0, await audit.PruneAsync(TimeSpan.FromDays(1))); // nothing old enough
                return true;
            });
        }
        finally
        {
            await host.InScopeAsync(async sp =>
            {
                await sp.GetRequiredService<SecurityDbContext>().Database.EnsureDeletedAsync();
                return true;
            });
        }
    }
}
