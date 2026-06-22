using App.Application;
using App.Application.Abstractions;
using App.Application.Provisioning;
using App.Infrastructure.Local;
using App.Infrastructure.Remote;
using App.UI;
using App.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// The Blazor Server host reuses App.UI verbatim — this is also the §15 web-port proof and the
// Playwright E2E target. A local SQLite working copy + a per-writer remote folder live under App_Data.
string dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services
    .AddApplication()
    .AddLocalStore(Path.Combine(dataDir, "local.db"))
    .AddRemoteStore(Path.Combine(dataDir, "remote"))
    .AddAppUi();

WebApplication app = builder.Build();

// Provision the local working copy: seed the entity catalog, then create tables/columns from it.
ICatalog catalog = app.Services.GetRequiredService<ICatalog>();
catalog.Seed(DefaultCatalog.Entries());
app.Services.GetRequiredService<ILocalStore>().EnsureSchema();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<AppRoot>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>Exposed so the Playwright E2E project can launch this host.</summary>
public partial class Program;
