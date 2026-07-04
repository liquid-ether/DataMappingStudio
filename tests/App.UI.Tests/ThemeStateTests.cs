using App.UI.Theme;

namespace App.UI.Tests;

/// <summary>The palette × mode model composing/parsing the data-theme attribute the stylesheet keys off.</summary>
public class ThemeStateTests
{
    [Theory]
    [InlineData("light", "teal", false)]
    [InlineData("dark", "teal", true)]                 // legacy stored values map to the default palette
    [InlineData("", "teal", false)]
    [InlineData("indigo", "indigo", false)]
    [InlineData("indigo-dark", "indigo", true)]
    [InlineData("ocean-dark", "ocean", true)]
    [InlineData("no-such-palette", "teal", false)]     // unknown values fall back safely
    public void Stored_values_parse_to_palette_and_mode(string stored, string palette, bool dark)
    {
        (string p, bool d) = ThemeState.Parse(stored);

        Assert.Equal(palette, p);
        Assert.Equal(dark, d);
    }

    [Theory]
    [InlineData("teal", false, "light")]
    [InlineData("teal", true, "dark")]
    [InlineData("graphite", false, "graphite")]
    [InlineData("graphite", true, "graphite-dark")]
    public void Palette_and_mode_compose_the_data_theme_value(string palette, bool dark, string expected)
    {
        // Round-trip: composing then parsing yields the same pair.
        (string p, bool d) = ThemeState.Parse(expected);
        Assert.Equal(palette, p);
        Assert.Equal(dark, d);
    }
}
