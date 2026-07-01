using App.Infrastructure.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Identity.Tests;

/// <summary>
/// Spins up the security module over a throwaway SQLite store (the default provider), seeded like a real
/// first run. Each test gets an isolated DB file, disposed + deleted at the end.
/// </summary>
public sealed class SecurityTestHost : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public const string AdminPassword = "Admin!Passw0rd1";

    public SecurityTestHost(Action<Dictionary<string, string?>>? configure = null)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dms-sec-" + Guid.NewGuid().ToString("N") + ".db");

        Dictionary<string, string?> settings = new()
        {
            ["Auth:Require"] = "true",
            ["Auth:Store:Provider"] = "Sqlite",
            ["Auth:BootstrapAdmin:UserName"] = "admin",
            ["Auth:BootstrapAdmin:Password"] = AdminPassword,
        };
        configure?.Invoke(settings);

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecurity(configuration, _dbPath);
        _provider = services.BuildServiceProvider();
    }

    public IServiceProvider Services => _provider;

    public Task SeedAsync() => _provider.InitializeSecurityAsync();

    /// <summary>Resolve scoped services for one logical operation.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
