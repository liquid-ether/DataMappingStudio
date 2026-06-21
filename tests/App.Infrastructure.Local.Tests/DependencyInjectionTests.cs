using App.Application;
using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Local.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddLocalStore_with_path_wires_a_working_store()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dms-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "local.db");

        try
        {
            ServiceProvider provider = new ServiceCollection()
                .AddApplication()
                .AddLocalStore(path)
                .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

            ICatalog catalog = provider.GetRequiredService<ICatalog>();
            ILocalStore store = provider.GetRequiredService<ILocalStore>();

            catalog.Seed(LocalStoreFixture.SeedCatalog());
            store.EnsureSchema();

            Guid id = Guid.NewGuid();
            store.Upsert(LocalStoreFixture.Table, new Row(LocalStoreFixture.Table, id) { ["name"] = "Resolved" }, "cs1", "alice");

            Assert.Equal("Resolved", store.GetById(LocalStoreFixture.Table, id)!["name"]);

            provider.Dispose();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
