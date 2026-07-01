namespace App.Infrastructure.Identity;

/// <summary>Binds the host's <c>Auth</c> configuration section.</summary>
public sealed class SecurityOptions
{
    /// <summary>When true, every endpoint requires an authenticated user; when false the host runs as a guest admin.</summary>
    public bool Require { get; set; }

    public StoreOptions Store { get; set; } = new();

    public DataProtectionOptions DataProtection { get; set; } = new();

    /// <summary>Seed admin created only on first run when the store has no users.</summary>
    public BootstrapAdminOptions? BootstrapAdmin { get; set; }

    public ProvidersOptions Providers { get; set; } = new();
}

public sealed class StoreOptions
{
    /// <summary>"Sqlite" (default) or "SqlServer".</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>Explicit connection string; when empty for Sqlite a default path under the data dir is used.</summary>
    public string? ConnectionString { get; set; }
}

public sealed class DataProtectionOptions
{
    /// <summary>Shared across hosts so cookies issued by one host are accepted by another.</summary>
    public string ApplicationName { get; set; } = "MappingStudio";
}

public sealed class BootstrapAdminOptions
{
    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string? Email { get; set; }
}

public sealed class ProvidersOptions
{
    public LocalProviderOptions Local { get; set; } = new();

    /// <summary>Zero or more OpenID Connect providers (Entra ID / Google / Okta / Auth0 / generic).</summary>
    public List<OidcProviderOptions> Oidc { get; set; } = [];
}

/// <summary>One OpenID Connect provider. Enabled ones surface on the login page and can auto-provision users.</summary>
public sealed class OidcProviderOptions
{
    /// <summary>Stable scheme id (also the external-login provider key). Keep short + URL-safe, e.g. "entra".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Button label on the login page (defaults to <see cref="Name"/>).</summary>
    public string? DisplayName { get; set; }

    public bool Enabled { get; set; } = true;

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    /// <summary>Set via user-secrets / env / Key Vault, not appsettings. Empty for public (PKCE-only) clients.</summary>
    public string? ClientSecret { get; set; }

    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];

    /// <summary>The claim carrying IdP groups/roles to map (e.g. "groups" or "roles"). Empty = no mapping.</summary>
    public string? RoleClaimType { get; set; }

    /// <summary>Maps an IdP group/role claim value to an app role (granted additively at each sign-in).</summary>
    public Dictionary<string, string> GroupRoleMap { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Create a local account on first external sign-in (JIT). Off = only pre-provisioned/linked users.</summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>Role granted to a JIT-provisioned user.</summary>
    public string DefaultRole { get; set; } = "Reader";
}

public sealed class LocalProviderOptions
{
    public bool Enabled { get; set; } = true;

    public bool AllowSelfRegistration { get; set; }

    public PasswordPolicyOptions Password { get; set; } = new();

    public LockoutPolicyOptions Lockout { get; set; } = new();
}

public sealed class PasswordPolicyOptions
{
    public int MinLength { get; set; } = 12;

    public bool RequireDigit { get; set; } = true;

    public bool RequireUppercase { get; set; } = true;

    public bool RequireLowercase { get; set; } = true;

    public bool RequireNonAlphanumeric { get; set; }
}

public sealed class LockoutPolicyOptions
{
    public int MaxFailed { get; set; } = 5;

    public int Minutes { get; set; } = 15;
}
