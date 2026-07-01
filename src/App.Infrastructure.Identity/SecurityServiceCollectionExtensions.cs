using App.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Identity;

/// <summary>
/// Wires the security module: the isolated (provider-pluggable) Identity store, cookie authentication,
/// password/lockout policy, permission-claims expansion, shared Data Protection keys, one authorization
/// policy per permission, and the admin/audit services. Call only when authentication is required; the
/// host runs as a guest admin otherwise (see <c>App.Web/Program.cs</c>).
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration configuration, string defaultSqlitePath)
    {
        IConfigurationSection section = configuration.GetSection("Auth");
        services.Configure<SecurityOptions>(section);
        SecurityOptions options = section.Get<SecurityOptions>() ?? new SecurityOptions();

        services.AddDbContext<SecurityDbContext>(db =>
        {
            if (string.Equals(options.Store.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                db.UseSqlServer(options.Store.ConnectionString
                    ?? throw new InvalidOperationException("Auth:Store:ConnectionString is required when Provider=SqlServer."));
            }
            else
            {
                string connection = string.IsNullOrWhiteSpace(options.Store.ConnectionString)
                    ? $"Data Source={defaultSqlitePath}"
                    : options.Store.ConnectionString!;
                db.UseSqlite(connection);
            }
        });

        // Shared key ring so cookies issued by one host are accepted by another (multi-host).
        services.AddDataProtection()
            .PersistKeysToDbContext<SecurityDbContext>()
            .SetApplicationName(options.DataProtection.ApplicationName);

        services.AddIdentityCore<AppUser>(id =>
            {
                PasswordPolicyOptions p = options.Providers.Local.Password;
                id.Password.RequiredLength = p.MinLength;
                id.Password.RequireDigit = p.RequireDigit;
                id.Password.RequireUppercase = p.RequireUppercase;
                id.Password.RequireLowercase = p.RequireLowercase;
                id.Password.RequireNonAlphanumeric = p.RequireNonAlphanumeric;

                LockoutPolicyOptions l = options.Providers.Local.Lockout;
                id.Lockout.AllowedForNewUsers = true;
                id.Lockout.MaxFailedAccessAttempts = l.MaxFailed;
                id.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(l.Minutes);

                id.User.RequireUniqueEmail = false;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<SecurityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, PermissionClaimsPrincipalFactory>();

        AuthenticationBuilder authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();

        // External OpenID Connect providers (Entra / Google / Okta / generic). Each signs into the Identity
        // external cookie; ExternalSignInService then links/provisions a local user on the callback.
        foreach (OidcProviderOptions oidc in options.Providers.Oidc.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Authority)))
        {
            authentication.AddOpenIdConnect(oidc.Name, oidc.DisplayName ?? oidc.Name, o =>
            {
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.Authority = oidc.Authority;
                o.ClientId = oidc.ClientId;
                o.ClientSecret = oidc.ClientSecret;
                o.ResponseType = "code";
                o.UsePkce = true;
                o.CallbackPath = $"/signin-oidc/{oidc.Name}";
                o.SaveTokens = false;
                o.GetClaimsFromUserInfoEndpoint = true;
                o.MapInboundClaims = false; // keep raw claim types (email, name, groups)
                o.Scope.Clear();
                foreach (string scope in oidc.Scopes)
                {
                    o.Scope.Add(scope);
                }

                o.TokenValidationParameters.NameClaimType = "name";
                if (oidc.RoleClaimType is { Length: > 0 } roleClaim)
                {
                    o.TokenValidationParameters.RoleClaimType = roleClaim;
                }
            });
        }
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.LoginPath = "/account/login";
            cookie.LogoutPath = "/account/logout";
            cookie.AccessDeniedPath = "/account/denied";
            cookie.Cookie.Name = "MappingStudio.Auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;
        });

        services.AddAuthorization(auth =>
        {
            foreach (string permission in Permissions.All)
            {
                auth.AddPolicy(permission, policy => policy.RequireClaim(Permissions.ClaimType, permission));
            }

            auth.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        services.AddScoped<IUserDirectory, IdentityUserDirectory>();
        services.AddScoped<ISecurityAudit, EfSecurityAudit>();
        services.AddScoped<ExternalSignInService>();
        services.AddSingleton<IAuthProviderCatalog, AuthProviderCatalog>();
        return services;
    }

    /// <summary>Ensures the store exists and seeds system roles + the bootstrap admin. Call before Run().</summary>
    public static Task InitializeSecurityAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        => SecuritySeeder.SeedAsync(services, cancellationToken);
}
