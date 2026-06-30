using App.Application;
using App.Application.Abstractions;
using App.Application.Provisioning;
using App.Infrastructure.Local;
using App.Infrastructure.Remote;
using App.UI;
using App.Web;
using App.Web.Components;
using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);

// The Blazor Server host reuses App.UI verbatim — this is also the §15 web-port proof and the
// Playwright E2E target. A local SQLite working copy + a per-writer remote folder live under App_Data.
string dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);

// The shared (synced) folder; set "RemoteFolder" (appsettings.json or the RemoteFolder env var) to a
// OneDrive/SharePoint-synced folder to collaborate, otherwise a local folder under App_Data is used.
string remoteFolder = builder.Configuration["RemoteFolder"] is { Length: > 0 } shared
    ? shared
    : Path.Combine(dataDir, "remote");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services
    .AddApplication()
    .AddLocalStore(Path.Combine(dataDir, "local.db"))
    .AddRemoteStore(remoteFolder)
    .AddAppUi();

// Record the authenticated request user as the change author (overrides the OS-account default).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// Ops health probe (anonymous) at /health: local working copy reachable + shared-folder status.
builder.Services.AddHealthChecks().AddCheck<WorkingCopyHealthCheck>("working-copy");

// Authentication is OFF by default (so local dev and the E2E suite work without credentials). In
// production set Auth:Require=true (or env Auth__Require=true): the host then requires an authenticated
// Windows user (Negotiate / Kerberos / NTLM) for every endpoint, and that identity feeds ICurrentUser.
bool requireAuth = builder.Configuration.GetValue("Auth:Require", false);
if (requireAuth)
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
        new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}

WebApplication app = builder.Build();

// Log the effective configuration so an operator can confirm where data lives and how auth is set.
app.Logger.LogInformation(
    "Mapping Studio web host starting. dataDir={DataDir}; remoteFolder={RemoteFolder}; authRequired={Auth}; seedSampleData={Seed}",
    dataDir, remoteFolder, requireAuth, app.Configuration.GetValue("SeedSampleData", false));

// Fail fast (clearly) if the shared folder is misconfigured: probe that it is writable. The app can
// still run locally, but publishing/sync would be broken — surface it at startup, not silently later.
try
{
    Directory.CreateDirectory(remoteFolder);
    string probe = Path.Combine(remoteFolder, ".write-probe");
    File.WriteAllText(probe, "ok");
    File.Delete(probe);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Shared folder '{RemoteFolder}' is not writable; publishing/sync will be unavailable until this is fixed.", remoteFolder);
}

// Provision the local working copy: seed the entity catalog, then create tables/columns from it.
ICatalog catalog = app.Services.GetRequiredService<ICatalog>();
catalog.Seed(DefaultCatalog.Entries());
app.Services.GetRequiredService<ILocalStore>().EnsureSchema();

// Optionally seed the demo dataset on a fresh store (off by default so tests/CI stay deterministic):
// set "SeedSampleData=true" (appsettings or the SeedSampleData env var) to populate all tables.
if (app.Configuration.GetValue("SeedSampleData", false))
{
    int seeded = new SampleDataSeeder(app.Services.GetRequiredService<LocalDatabase>()).SeedIfEmpty();
    app.Logger.LogInformation("Seeded {Count} sample rows.", seeded);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
if (requireAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/health").AllowAnonymous(); // reachable even when Auth:Require is on (for probes)
app.MapRazorComponents<AppRoot>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>Exposed so the Playwright E2E project can launch this host.</summary>
public partial class Program;
