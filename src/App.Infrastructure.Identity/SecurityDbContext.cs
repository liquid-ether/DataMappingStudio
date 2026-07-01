using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

/// <summary>
/// The isolated security store: ASP.NET Core Identity schema + the security audit trail + the shared
/// Data Protection key ring (so multiple hosts decrypt each other's auth cookies). Provider-agnostic —
/// the concrete provider (SQLite / SQL Server) is chosen in <see cref="SecurityServiceCollectionExtensions"/>.
/// </summary>
public sealed class SecurityDbContext(DbContextOptions<SecurityDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SecurityAuditEvent>(e =>
        {
            e.ToTable("SecurityAuditEvents");
            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => x.UserName);
            e.Property(x => x.Event).HasMaxLength(64);
            e.Property(x => x.UserName).HasMaxLength(256);
            e.Property(x => x.Detail).HasMaxLength(1024);
            e.Property(x => x.IpAddress).HasMaxLength(64);
        });

        builder.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Origin).HasMaxLength(64);
        });

        builder.Entity<AppRole>(e => e.Property(x => x.Description).HasMaxLength(512));
    }
}
