using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using App.Application;
using App.Application.Abstractions;
using App.Application.Provisioning;
using App.Infrastructure.Local;
using App.Infrastructure.Remote;
using App.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Detailed log file per launch, under %LOCALAPPDATA%\MappingStudio\logs. Captures startup,
        // provisioning, and (via the logging pipeline) Blazor component exceptions that show the error UI.
        string logDir = Path.Combine(dataDir, "logs");
        Directory.CreateDirectory(logDir);
        FileLoggerProvider logProvider = new(Path.Combine(logDir, $"desktop-{DateTime.Now:yyyyMMdd-HHmmss}.log"));
        InstallGlobalExceptionHandlers(logProvider);

        try
        {
            logProvider.Append($"{DateTimeOffset.Now:O} [Information] Startup: data dir {dataDir}");

            // The shared (synced) folder. Set the MAPPINGSTUDIO_REMOTE environment variable to your
            // OneDrive/SharePoint-synced folder to collaborate; otherwise a private local folder is used.
            string remoteFolder = Environment.GetEnvironmentVariable("MAPPINGSTUDIO_REMOTE") is { Length: > 0 } shared
                ? shared
                : Path.Combine(dataDir, "remote");

            ServiceCollection services = new();
            services.AddWpfBlazorWebView();
            services.AddLogging(builder =>
            {
                builder.AddProvider(logProvider);
                builder.SetMinimumLevel(LogLevel.Information);
            });
            services
                .AddApplication()
                .AddLocalStore(Path.Combine(dataDir, "local.db"))
                .AddRemoteStore(remoteFolder)
                .AddAppUi();

            // Runtime settings: shared scope via the synced folder, local scope beside the working copy.
            services.AddSingleton<App.Application.Abstractions.IRuntimeConfig>(sp =>
                new App.Application.Configuration.RuntimeConfigService(
                    sp.GetService<App.Application.Abstractions.ISharedSettingsStore>(),
                    Path.Combine(dataDir, "app_config.local.json")));

            ServiceProvider provider = services.BuildServiceProvider();
            Resources.Add("services", provider);

            // Provision the local working copy.
            provider.GetRequiredService<ICatalog>().Seed(DefaultCatalog.Entries());
            provider.GetRequiredService<ITableCatalog>().SeedTableMeta(DefaultCatalog.TableMeta());
            provider.GetRequiredService<ILocalStore>().EnsureSchema();

            // Seed the demo dataset only when explicitly requested (MAPPINGSTUDIO_SEED_SAMPLE=true) — a
            // production install must start clean (and then import the real workbook), not with demo data.
            bool seedSample = string.Equals(
                Environment.GetEnvironmentVariable("MAPPINGSTUDIO_SEED_SAMPLE"), "true", StringComparison.OrdinalIgnoreCase);
            int seeded = seedSample ? new SampleDataSeeder(provider.GetRequiredService<LocalDatabase>()).SeedIfEmpty() : 0;
            logProvider.Append($"{DateTimeOffset.Now:O} [Information] Provisioned local store (seeded {seeded} sample rows, sampleData={seedSample}); opening window.");

            new MainWindow(provider).Show();
        }
        catch (Exception ex)
        {
            logProvider.Append($"{DateTimeOffset.Now:O} [Critical] Startup failed: {ex}");
            throw;
        }
    }

    private void InstallGlobalExceptionHandlers(FileLoggerProvider log)
    {
        DispatcherUnhandledException += (_, args) =>
            log.Append($"{DateTimeOffset.Now:O} [Error] DispatcherUnhandledException: {args.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Append($"{DateTimeOffset.Now:O} [Critical] AppDomain.UnhandledException: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
            log.Append($"{DateTimeOffset.Now:O} [Error] UnobservedTaskException: {args.Exception}");
    }
}
