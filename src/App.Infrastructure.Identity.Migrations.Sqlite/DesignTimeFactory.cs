using App.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace App.Infrastructure.Identity.Migrations.Sqlite;

/// <summary>Design-time factory so `dotnet ef migrations add` can build the SQLite model.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SecurityDbContext>
{
    public SecurityDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SecurityDbContext> options = new();
        options.UseSqlite("Data Source=design-time.db", b => b.MigrationsAssembly(typeof(DesignTimeFactory).Assembly.GetName().Name));
        return new SecurityDbContext(options.Options);
    }
}
