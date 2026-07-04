using Microsoft.JSInterop;

namespace App.UI.Theme;

/// <summary>
/// App-wide theme: a colour <see cref="Palette"/> (teal default, plus indigo / graphite / ocean / paper)
/// crossed with a light/dark <see cref="IsDark"/> mode. The pair composes into a single
/// <c>data-theme</c> attribute on the document's root element (set by the host page's bootstrap script
/// and updated here via JS interop), which the token sheet (<c>app.css</c>) keys off — teal keeps the
/// legacy values <c>light</c>/<c>dark</c>, other palettes use <c>&lt;palette&gt;[-dark]</c>. Persists in
/// <c>localStorage</c> and raises <see cref="OnChange"/> so the chrome re-renders. Scoped per circuit;
/// shared by the web and desktop hosts.
/// </summary>
public sealed class ThemeState(IJSRuntime js)
{
    public const string DefaultPalette = "teal";

    /// <summary>Selectable palettes, in display order.</summary>
    public static readonly IReadOnlyList<string> Palettes = ["teal", "indigo", "graphite", "ocean", "paper"];

    private readonly IJSRuntime _js = js;

    public string Palette { get; private set; } = DefaultPalette;

    public bool IsDark { get; private set; }

    /// <summary>The composed <c>data-theme</c> value the stylesheet keys off.</summary>
    public string Current => Palette == DefaultPalette
        ? (IsDark ? "dark" : "light")
        : (IsDark ? $"{Palette}-dark" : Palette);

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
                (Palette, IsDark) = Parse(theme);
            }
        }
        catch
        {
            // JS not ready (e.g. prerender) — keep the default and let a later call retry.
            Ready = false;
        }

        OnChange?.Invoke();
    }

    /// <summary>Flips light/dark within the current palette.</summary>
    public Task ToggleAsync() => ApplyAsync(Palette, !IsDark);

    /// <summary>Switches the colour palette, keeping the current light/dark mode.</summary>
    public Task SetPaletteAsync(string palette)
        => ApplyAsync(Palettes.Contains(palette, StringComparer.OrdinalIgnoreCase) ? palette.ToLowerInvariant() : DefaultPalette, IsDark);

    /// <summary>Applies a raw <c>data-theme</c> value (legacy entry point; also used by tests).</summary>
    public Task SetAsync(string theme)
    {
        (string palette, bool dark) = Parse(theme);
        return ApplyAsync(palette, dark);
    }

    private async Task ApplyAsync(string palette, bool dark)
    {
        Palette = palette;
        IsDark = dark;
        try
        {
            await _js.InvokeVoidAsync("dmsTheme.apply", Current);
        }
        catch
        {
            // Ignore — the attribute will be re-applied on the next interactive render.
        }

        OnChange?.Invoke();
    }

    /// <summary>Decomposes any stored <c>data-theme</c> value (legacy "light"/"dark" map to teal).</summary>
    public static (string Palette, bool IsDark) Parse(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        if (v is "" or "light") { return (DefaultPalette, false); }
        if (v == "dark") { return (DefaultPalette, true); }

        bool dark = v.EndsWith("-dark", StringComparison.Ordinal);
        string palette = dark ? v[..^"-dark".Length] : v;
        return Palettes.Contains(palette) ? (palette, dark) : (DefaultPalette, dark);
    }
}
