using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using App.Application;
using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Infrastructure.Local;
using App.Infrastructure.Remote;
using App.UI;
using Microsoft.Extensions.DependencyInjection;

namespace App.Desktop;

/// <summary>
/// Interaction logic for App.xaml (the Blazor Hybrid desktop shell).
/// Named <c>DesktopApp</c> rather than <c>App</c>: a type named <c>App</c> would collide with the
/// <c>App.*</c> root namespace in WPF-generated code. The base type is fully qualified because the
/// <c>App.Application</c> project makes the unqualified name <c>Application</c> resolve to that namespace.
/// </summary>
public partial class DesktopApp : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ServiceCollection services = new();
        services.AddWpfBlazorWebView();

        string dataDir = Path.Combine(AppContext.BaseDirectory, "App_Data");
        Directory.CreateDirectory(dataDir);
        services
            .AddApplication()
            .AddLocalStore(Path.Combine(dataDir, "local.db"))
            .AddRemoteStore(Path.Combine(dataDir, "remote"))
            .AddAppUi();

        ServiceProvider provider = services.BuildServiceProvider();

        // Provision the local working copy.
        provider.GetRequiredService<ICatalog>().Seed(DemoCatalog());
        provider.GetRequiredService<ILocalStore>().EnsureSchema();

        new MainWindow(provider).Show();
    }

    private static IReadOnlyList<ColumnCatalogEntry> DemoCatalog() =>
    [
        new() { TableName = "widget", ColumnName = "name", ValueType = CatalogValueType.Text, IsRequired = true, LabelEn = "Name", LabelFr = "Nom", DisplayOrder = 1 },
        new() { TableName = "widget", ColumnName = "qty", ValueType = CatalogValueType.Integer, LabelEn = "Quantity", LabelFr = "Quantité", DisplayOrder = 2 },
        new() { TableName = "widget", ColumnName = "price", ValueType = CatalogValueType.Number, LabelEn = "Price", LabelFr = "Prix", DisplayOrder = 3 },
        new() { TableName = "widget", ColumnName = "active", ValueType = CatalogValueType.Boolean, LabelEn = "Active", LabelFr = "Actif", DisplayOrder = 4 },
    ];
}
