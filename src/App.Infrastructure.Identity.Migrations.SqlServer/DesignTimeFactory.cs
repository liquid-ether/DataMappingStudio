using App.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace App.Infrastructure.Identity.Migrations.SqlServer;

/// <summary>Design-time factory so `dotnet ef migrations add` can build the SQL Server model.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SecurityDbContext>
{
    public SecurityDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SecurityDbContext> options = new();
        options.UseSqlServer("Server=(designtime);Database=design;Trusted_Connection=True;", b => b.MigrationsAssembly(typeof(DesignTimeFactory).Assembly.GetName().Name));
        return new SecurityDbContext(options.Options);
    }
}
