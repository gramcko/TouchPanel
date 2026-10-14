using TouchPanel.Config;
using TouchPanel.Theme;

namespace TouchPanel.Tests.Theme;

/// <summary>
/// Per-theme appearance-default table (M5-RF-540): every built-in theme defines in-bounds values
/// (colors, font size, opacity), dark reproduces the delivered palette, and an unknown/missing theme
/// falls back to dark.
/// </summary>
public sealed class ThemeAppearanceDefaultsTests
{
    [Theory]
    [InlineData(ThemeIds.Dark)]
    [InlineData(ThemeIds.Light)]
    [InlineData(ThemeIds.HighContrast)]
    public void EveryTheme_HasInBoundsValues(string themeId)
    {
        var defaults = ThemeAppearanceDefaults.For(themeId);

        Assert.True(WidgetAppearance.IsValidColor(defaults.Foreground));
        Assert.True(WidgetAppearance.IsValidColor(defaults.Background));
        Assert.True(WidgetAppearance.IsValidColor(defaults.Accent));

        Assert.Equal(defaults.FontSize, WidgetAppearance.NormalizeFontSize(defaults.FontSize));
        Assert.InRange(defaults.FontSize, WidgetAppearance.MinFontSize, WidgetAppearance.MaxFontSize);
        Assert.InRange(defaults.Opacity, WidgetAppearance.MinOpacity, WidgetAppearance.MaxOpacity);
    }

    [Fact]
    public void Dark_ReproducesTheDeliveredPalette()
    {
        var dark = ThemeAppearanceDefaults.For(ThemeIds.Dark);

        Assert.Equal("#EDEDED", dark.Foreground);
        Assert.Equal("#2A2A30", dark.Background);
        Assert.Equal("#3D7EFF", dark.Accent);
    }

    [Fact]
    public void HighContrast_PrefersLargerType()
    {
        Assert.True(
            ThemeAppearanceDefaults.For(ThemeIds.HighContrast).FontSize
            >= ThemeAppearanceDefaults.For(ThemeIds.Dark).FontSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("solarized")]
    public void UnknownTheme_FallsBackToDark(string? themeId)
    {
        Assert.Equal(ThemeAppearanceDefaults.Dark, ThemeAppearanceDefaults.For(themeId));
    }

    [Fact]
    public void EveryBuiltInTheme_HasADefinedEntry()
    {
        foreach (var themeId in ThemeIds.All)
        {
            Assert.True(ThemeAppearanceDefaults.TryGet(themeId, out var defaults));
            Assert.NotNull(defaults);
        }
    }
}
