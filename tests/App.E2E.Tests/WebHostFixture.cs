using System.Diagnostics;
using System.Net.Sockets;

namespace App.E2E.Tests;

/// <summary>
/// Launches the real <c>App.Web</c> Blazor Server host on a free port (Kestrel, not the in-memory
/// TestServer, so Playwright can drive a real browser against it). Enabled only when <c>DMS_E2E=1</c>
/// so the suite stays green where browsers aren't installed; CI sets the var after <c>playwright install</c>.
/// </summary>
public sealed class WebHostFixture : IAsyncLifetime
{
    private Process? _process;

    public static bool Enabled => Environment.GetEnvironmentVariable("DMS_E2E") == "1";

    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        int port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        string dll = typeof(Program).Assembly.Location;

        _process = Process.Start(new ProcessStartInfo("dotnet", $"exec \"{dll}\" --urls {BaseUrl} --environment Development")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        }) ?? throw new InvalidOperationException("Failed to start App.Web.");

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(3) };
        for (int i = 0; i < 40; i++)
        {
            try
            {
                if ((await client.GetAsync(BaseUrl)).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                await Task.Delay(500);
            }
        }

        throw new InvalidOperationException("App.Web did not become ready.");
    }

    public Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }

        _process?.Dispose();
        return Task.CompletedTask;
    }

    private static int FreePort()
    {
        using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
