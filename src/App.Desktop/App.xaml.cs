using System;
using System.IO;
using System.Windows;
using App.Application;
using App.Application.Abstractions;
using App.Application.Provisioning;
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

        // Per-user data (SQLite working copy + the default remote folder) lives under %LOCALAPPDATA%,
        // so the app runs from anywhere (incl. Program Files) without needing write access beside the exe.
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MappingStudio");
        Directory.CreateDirectory(dataDir);
        string hostPage = ExtractHostPage(dataDir);

        // The shared (synced) folder. Set the MAPPINGSTUDIO_REMOTE environment variable to your
        // OneDrive/SharePoint-synced folder to collaborate; otherwise a private local folder is used.
        string remoteFolder = Environment.GetEnvironmentVariable("MAPPINGSTUDIO_REMOTE") is { Length: > 0 } shared
            ? shared
            : Path.Combine(dataDir, "remote");

        ServiceCollection services = new();
        services.AddWpfBlazorWebView();
        services
            .AddApplication()
            .AddLocalStore(Path.Combine(dataDir, "local.db"))
            .AddRemoteStore(remoteFolder)
            .AddAppUi();

        ServiceProvider provider = services.BuildServiceProvider();

        // Provision the local working copy.
        provider.GetRequiredService<ICatalog>().Seed(DefaultCatalog.Entries());
        provider.GetRequiredService<ILocalStore>().EnsureSchema();

        new MainWindow(provider, hostPage).Show();
    }

    /// <summary>Extracts the embedded WebView host page next to the per-user data (single-file friendly).</summary>
    private static string ExtractHostPage(string dataDir)
    {
        string wwwroot = Path.Combine(dataDir, "wwwroot");
        Directory.CreateDirectory(wwwroot);
        string path = Path.Combine(wwwroot, "index.html");

        using Stream resource = typeof(DesktopApp).Assembly.GetManifestResourceStream("index.html")
            ?? throw new InvalidOperationException("Embedded host page 'index.html' was not found.");
        using FileStream file = File.Create(path);
        resource.CopyTo(file);

        return path;
    }
}
