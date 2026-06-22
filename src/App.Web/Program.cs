using App.Application;
using App.Application.Abstractions;
using App.Domain.Catalog;
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

// Provision the local working copy: seed the catalog, then create tables/columns from it.
ICatalog catalog = app.Services.GetRequiredService<ICatalog>();
catalog.Seed(DemoCatalog());
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

// Demo catalog so the metadata-driven grid renders something in the running app. The full entity
// catalog (application/data_source/…) is seeded in v0.7.0.
static IReadOnlyList<ColumnCatalogEntry> DemoCatalog() =>
[
    new() { TableName = "widget", ColumnName = "name", ValueType = CatalogValueType.Text, IsRequired = true, LabelEn = "Name", LabelFr = "Nom", DisplayOrder = 1 },
    new() { TableName = "widget", ColumnName = "qty", ValueType = CatalogValueType.Integer, LabelEn = "Quantity", LabelFr = "Quantité", DisplayOrder = 2 },
    new() { TableName = "widget", ColumnName = "price", ValueType = CatalogValueType.Number, LabelEn = "Price", LabelFr = "Prix", DisplayOrder = 3 },
    new() { TableName = "widget", ColumnName = "active", ValueType = CatalogValueType.Boolean, LabelEn = "Active", LabelFr = "Actif", DisplayOrder = 4 },
];

/// <summary>Exposed so the Playwright E2E project can host the app via WebApplicationFactory.</summary>
public partial class Program;
