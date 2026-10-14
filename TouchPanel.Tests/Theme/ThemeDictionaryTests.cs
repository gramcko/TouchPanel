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
            var light = Load("/TouchPanel;component/Theme/Themes/Light.xaml");
            var highContrast = Load("/TouchPanel;component/Theme/Themes/HighContrast.xaml");
            var dark = Load("/TouchPanel;component/Theme/Themes/Dark.xaml");

            // The dark theme reproduces the current palette exactly (its values live in Colors.xaml
            // and are merged in), so applying the default is a no-op.
            foreach (var key in darkSource.Keys)
            {
                Assert.True(dark.Contains(key), $"Dark theme is missing palette key '{key}'.");
            }

            // Every theme defines the same key set; only values differ.
            foreach (var key in darkSource.Keys)
            {
                Assert.True(light.Contains(key), $"Light theme is missing palette key '{key}'.");
                Assert.True(highContrast.Contains(key), $"HighContrast theme is missing palette key '{key}'.");
            }

            Assert.Equal(ThemeIds.Dark, dark[ThemeIds.ThemeIdKey]);
            Assert.Equal(ThemeIds.Light, light[ThemeIds.ThemeIdKey]);
            Assert.Equal(ThemeIds.HighContrast, highContrast[ThemeIds.ThemeIdKey]);
        });
    }

    [Fact]
    public async Task BuiltInThemes_UseDistinctSurfaceAndBackgroundColors()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var dark = Load("/TouchPanel;component/Theme/Colors.xaml");
            var light = Load("/TouchPanel;component/Theme/Themes/Light.xaml");
            var highContrast = Load("/TouchPanel;component/Theme/Themes/HighContrast.xaml");

            var darkSurface = (Color)dark["WidgetSurfaceColor"];
            var lightSurface = (Color)light["WidgetSurfaceColor"];
            var highContrastSurface = (Color)highContrast["WidgetSurfaceColor"];

            Assert.NotEqual(darkSurface, lightSurface);
            Assert.NotEqual(darkSurface, highContrastSurface);
            Assert.NotEqual(lightSurface, highContrastSurface);

            var darkBackground = (Color)dark["PanelBackgroundColor"];
            Assert.NotEqual(darkBackground, (Color)light["PanelBackgroundColor"]);
            Assert.NotEqual(darkBackground, (Color)highContrast["PanelBackgroundColor"]);
        });
    }

    private static ResourceDictionary Load(string componentUri) =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(new Uri(componentUri, UriKind.Relative)));
}
