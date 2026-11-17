using System.Windows;
using System.Windows.Media;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Theme;

namespace TouchPanel.Tests.Theme;

/// <summary>
/// M3-RF-842/M3-RF-849 coverage for the built-in theme dictionaries: each theme carries the stable
/// id marker and defines the same palette key set as the Core-Release dark source
/// (<c>Theme/Colors.xaml</c>), with distinct values so a swap is observable.
/// </summary>
public sealed class ThemeDictionaryTests
{
    [Fact]
    public async Task BuiltInThemes_DefineTheSamePaletteKeysAsTheDarkSource()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var darkSource = Load("/TouchPanel;component/Theme/Colors.xaml");
            var dark = Load("/TouchPanel;component/Theme/Themes/Dark.xaml");

            // The dark theme reproduces the current palette exactly (its values live in Colors.xaml
            // and are merged in), so applying the default is a no-op.
            foreach (var key in darkSource.Keys)
            {
                Assert.True(dark.Contains(key), $"Dark theme is missing palette key '{key}'.");
            }

            // Every theme defines the same key set; only values differ.
            foreach (var themeId in ThemeIds.All)
            {
                var theme = LoadTheme(themeId);
                foreach (var key in darkSource.Keys)
                {
                    Assert.True(theme.Contains(key), $"Theme '{themeId}' is missing palette key '{key}'.");
                }

                Assert.Equal(themeId, theme[ThemeIds.ThemeIdKey]);
            }
        });
    }

    [Fact]
    public async Task BuiltInThemes_UseDistinctSurfaceAndBackgroundColors()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var dark = Load("/TouchPanel;component/Theme/Colors.xaml");
            var darkSurface = (Color)dark["WidgetSurfaceColor"];
            var darkBackground = (Color)dark["PanelBackgroundColor"];

            foreach (var themeId in ThemeIds.All.Where(id => id != ThemeIds.Dark))
            {
                var theme = LoadTheme(themeId);
                Assert.NotEqual(darkSurface, (Color)theme["WidgetSurfaceColor"]);
                Assert.NotEqual(darkBackground, (Color)theme["PanelBackgroundColor"]);
            }
        });
    }

    [Fact]
    public async Task NewThemes_AreReadableAgainstTheirSurfaces()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            foreach (var themeId in new[] { ThemeIds.Midnight, ThemeIds.Nord, ThemeIds.Oled })
            {
                var theme = LoadTheme(themeId);
                var foreground = (Color)theme["PanelForegroundColor"];
                var surface = (Color)theme["WidgetSurfaceColor"];

                // The same contrast relationship as dark/light: a light foreground on a dark surface.
                Assert.True(
                    RelativeLuminance(foreground) > RelativeLuminance(surface) + 0.25,
                    $"Theme '{themeId}' foreground is not clearly lighter than its surface.");
            }
        });
    }

    private static ResourceDictionary LoadTheme(string themeId)
    {
        Assert.True(ThemeIds.TryGetResourceUri(themeId, out var uri));
        return Assert.IsType<ResourceDictionary>(Application.LoadComponent(uri));
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var srgb = value / 255.0;
        return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static ResourceDictionary Load(string componentUri) =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(new Uri(componentUri, UriKind.Relative)));
}
