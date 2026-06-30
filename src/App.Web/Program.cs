using App.Application;
using App.Application.Abstractions;
using App.Infrastructure.Local;
using App.Infrastructure.Remote;
using App.UI;
using App.Web;
using App.Web.Components;
using App.Web.Workspaces;
using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);

// The Blazor Server host reuses App.UI verbatim — this is also the §15 web-port proof and the
// Playwright E2E target. Per-user working copies + the per-writer remote folder live under the data dir
// (configurable via "DataDir" so each host instance / test run can be isolated; default App_Data).
string dataDir = builder.Configuration["DataDir"] is { Length: > 0 } configuredDataDir
    ? configuredDataDir
    : Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);

// The shared (synced) folder; set "RemoteFolder" (appsettings.json or the RemoteFolder env var) to a
// OneDrive/SharePoint-synced folder to collaborate, otherwise a local folder under App_Data is used.
string remoteFolder = builder.Configuration["RemoteFolder"] is { Length: > 0 } shared
    ? shared
    : Path.Combine(dataDir, "remote");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services
    .AddApplication()
    .AddLocalStore()                                                   // workbook reader only; no shared DB
    .AddRemoteStore(remoteFolder, enableAutoRefresh: false, enableCoordinator: false) // shared remote; per-user coordinators
    .AddAppUi();

// Per-user workspaces: each authenticated user gets their own SQLite working copy + sync coordinator
// (writer id = user@host), so one host serves many users and many hosts can run against the shared
// folder concurrently. The store/catalog/coordinator/import services resolve per user from here.
builder.Services.AddScoped<ICurrentUser, CircuitCurrentUser>();
builder.Services.AddPerUserWorkspaces(Path.Combine(dataDir, "users"));

// Ops health probe (anonymous) at /health: shared-folder reachable + active-workspace count.
builder.Services.AddHealthChecks().AddCheck<WorkingCopyHealthCheck>("workspaces");

// Authentication is OFF by default (so local dev and the E2E suite work without credentials, collapsing
// to a single shared "dev" workspace). In production set Auth:Require=true (or env Auth__Require=true):
// the host then requires an authenticated Windows user (Negotiate) for every endpoint, and that identity
// keys each user's workspace and is recorded as the change author.
bool requireAuth = builder.Configuration.GetValue("Auth:Require", false);
if (requireAuth)
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
        new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    builder.Services.AddCascadingAuthenticationState(); // surfaces the user to the circuit (and to CircuitCurrentUser)
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

// Per-user working copies are provisioned on first use by the workspace registry (catalog seed +
// schema + initial refold from the shared folder) — there is no single shared store to provision here.

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
