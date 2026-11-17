using TouchPanel.Theme;

namespace TouchPanel.Tests.Theme;

/// <summary>
/// M3-RF-842/M3-RF-847 coverage for the built-in theme id allowlist: stable canonical ids,
/// case-insensitive matching, and unknown/missing fallback to dark.
/// </summary>
public sealed class ThemeIdsTests
{
    [Fact]
    public void All_ExposesTheBuiltInThemesInStableOrderWithDarkFirst()
    {
        Assert.Equal(
            new[]
            {
                ThemeIds.Dark,
                ThemeIds.Light,
                ThemeIds.HighContrast,
                ThemeIds.Midnight,
                ThemeIds.Nord,
                ThemeIds.Oled,
            },
            ThemeIds.All);
        Assert.Equal(ThemeIds.Dark, ThemeIds.All[0]);
        Assert.True(ThemeIds.All.Count >= 6);
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("DARK")]
    [InlineData("Light")]
    [InlineData("light")]
    [InlineData("highcontrast")]
    [InlineData("HIGHCONTRAST")]
    [InlineData("  highContrast  ")]
    [InlineData("midnight")]
    [InlineData("MIDNIGHT")]
    [InlineData("  Nord  ")]
    [InlineData("OLED")]
    public void IsKnown_IsCaseInsensitiveAndTrims(string id)
    {
        Assert.True(ThemeIds.IsKnown(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("solarized")]
    public void IsKnown_RejectsUnknownOrMissing(string? id)
    {
        Assert.False(ThemeIds.IsKnown(id));
    }

    [Theory]
    [InlineData("DARK", ThemeIds.Dark, true)]
    [InlineData("Light", ThemeIds.Light, true)]
    [InlineData("HIGHCONTRAST", ThemeIds.HighContrast, true)]
    [InlineData("Midnight", ThemeIds.Midnight, true)]
    [InlineData("NORD", ThemeIds.Nord, true)]
    [InlineData(" oled ", ThemeIds.Oled, true)]
    [InlineData("mystery", ThemeIds.Dark, false)]
    [InlineData(null, ThemeIds.Dark, false)]
    public void TryNormalize_ReturnsCanonicalIdOrFallsBackToDark(string? id, string expected, bool known)
    {
        Assert.Equal(known, ThemeIds.TryNormalize(id, out var canonical));
        Assert.Equal(expected, canonical);
        Assert.Equal(expected, ThemeIds.Normalize(id));
    }

    [Fact]
    public void TryGetResourceUri_ResolvesKnownIdsOnly()
    {
        Assert.True(ThemeIds.TryGetResourceUri("Light", out var lightUri));
        Assert.Contains("Theme/Themes/Light.xaml", lightUri.OriginalString, StringComparison.Ordinal);

        Assert.True(ThemeIds.TryGetResourceUri("MIDNIGHT", out var midnightUri));
        Assert.Contains("Theme/Themes/Midnight.xaml", midnightUri.OriginalString, StringComparison.Ordinal);

        Assert.True(ThemeIds.TryGetResourceUri(ThemeIds.Nord, out var nordUri));
        Assert.Contains("Theme/Themes/Nord.xaml", nordUri.OriginalString, StringComparison.Ordinal);

        Assert.True(ThemeIds.TryGetResourceUri(ThemeIds.Oled, out var oledUri));
        Assert.Contains("Theme/Themes/Oled.xaml", oledUri.OriginalString, StringComparison.Ordinal);

        Assert.False(ThemeIds.TryGetResourceUri("unknown", out _));
        Assert.False(ThemeIds.TryGetResourceUri(null!, out _));
    }
}
