using Microsoft.JSInterop;

namespace App.UI.Theme;

/// <summary>
/// App-wide light/dark theme. The actual switch is a <c>data-theme</c> attribute on the document's
/// root element (set by the host page's bootstrap script and toggled here via JS interop), which the
/// global token sheet (<c>app.css</c>) keys off. Persists the choice in <c>localStorage</c> so it
/// survives reloads, and raises <see cref="OnChange"/> so the top bar (and any other subscriber)
/// re-renders the toggle. Scoped per Blazor circuit; shared by the web and desktop hosts.
/// </summary>
public sealed class ThemeState(IJSRuntime js)
{
    private readonly IJSRuntime _js = js;

    public string Current { get; private set; } = "light";

    public bool IsDark => Current == "dark";

    /// <summary>True once the current theme has been read back from the browser.</summary>
    public bool Ready { get; private set; }

    public event Action? OnChange;

    /// <summary>
    /// Reads the theme the bootstrap script already applied to <c>&lt;html&gt;</c> and syncs our state
    /// to it. Idempotent: only the first call hits JS. Must run after the first render (JS interop is
    /// unavailable during prerender), e.g. from <c>OnAfterRenderAsync(firstRender)</c>.
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (Ready)
        {
            return;
        }

        Ready = true;
        try
        {
            string? theme = await _js.InvokeAsync<string?>("dmsTheme.get");
            if (!string.IsNullOrEmpty(theme))
            {
                Current = theme;
            }
        }
        catch
        {
            // JS not ready (e.g. prerender) — keep the default and let a later call retry.
            Ready = false;
        }

        OnChange?.Invoke();
    }

    public Task ToggleAsync() => SetAsync(IsDark ? "light" : "dark");

    public async Task SetAsync(string theme)
    {
        Current = theme;
        try
        {
            await _js.InvokeVoidAsync("dmsTheme.apply", theme);
        }
        catch
        {
            // Ignore — the attribute will be re-applied on the next interactive render.
        }

        OnChange?.Invoke();
    }
}
