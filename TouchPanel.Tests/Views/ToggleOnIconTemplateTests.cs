using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Theme;
using TouchPanel.ViewModels;
using TouchPanel.Views.Controls;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Real-BAML coverage for the user-requested toggle on-state icon: the toggle template binds the icon
/// presenter to the display-icon properties, so the glyph/vector swaps with <c>IsOn</c>. The active
/// surface is accent-filled, so the on icon must never inherit the accent again: it uses the
/// configured <see cref="ToggleWidgetConfig.OnIconColor"/> when set, otherwise the contrasting theme
/// default (<c>PanelBackgroundBrush</c>).
/// </summary>
public sealed class ToggleOnIconTemplateTests
{
    private const string BackgroundKey = "PanelBackgroundBrush";
    private const string ForegroundKey = "PanelForegroundBrush";
    private const string AccentKey = "PanelAccentBrush";

    [Fact]
    public async Task Template_OnStateWithGlyphOnIcon_UsesTheContrastingPanelBackgroundByDefault()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = CreateViewModel(initialOn: true, icon: "power", onIcon: "star");
            var presenter = Realize(viewModel);
            var glyph = Assert.IsType<TextBlock>(presenter.FindName("GlyphText"));

            return new
            {
                GlyphText = glyph.Text,
                Foreground = (presenter.Foreground as SolidColorBrush)?.Color,
                Background = ThemeColor(BackgroundKey),
                Accent = ThemeColor(AccentKey),
            };
        });

        Assert.Equal(IconCatalog.GlyphFor("star"), result.GlyphText);
        Assert.Equal(result.Background, result.Foreground);
        Assert.NotEqual(result.Accent, result.Foreground);
        Assert.NotEqual(IconCatalog.GlyphFor("power"), result.GlyphText);
    }

    [Fact]
    public async Task Template_OffStateWithGlyphOnIcon_KeepsTheBaseGlyphAndForeground()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = CreateViewModel(initialOn: false, icon: "power", onIcon: "star");
            var presenter = Realize(viewModel);
            var glyph = Assert.IsType<TextBlock>(presenter.FindName("GlyphText"));

            return new
            {
                GlyphText = glyph.Text,
                Foreground = (presenter.Foreground as SolidColorBrush)?.Color,
                PanelForeground = ThemeColor(ForegroundKey),
                Background = ThemeColor(BackgroundKey),
            };
        });

        Assert.Equal(IconCatalog.GlyphFor("power"), result.GlyphText);
        Assert.Equal(result.PanelForeground, result.Foreground);
        Assert.NotEqual(result.Background, result.Foreground);
    }

    [Fact]
    public async Task Template_OnStateWithVectorOnIcon_ShowsAStrokedPathInTheContrastingColour()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = CreateViewModel(initialOn: true, icon: "power", onIcon: "microphoneMuted");
            var presenter = Realize(viewModel);
            var stroked = Assert.IsType<Path>(presenter.FindName("StrokePath"));
            var filled = Assert.IsType<Path>(presenter.FindName("VectorPath"));
            var glyph = Assert.IsType<TextBlock>(presenter.FindName("GlyphText"));

            return new
            {
                GlyphVisibility = glyph.Visibility,
                FilledVisibility = filled.Visibility,
                StrokeVisibility = stroked.Visibility,
                StrokeBrush = (stroked.Stroke as SolidColorBrush)?.Color,
                HasGeometry = stroked.Data is not null,
                Background = ThemeColor(BackgroundKey),
            };
        });

        Assert.Equal(Visibility.Collapsed, result.GlyphVisibility);
        Assert.Equal(Visibility.Collapsed, result.FilledVisibility);
        Assert.Equal(Visibility.Visible, result.StrokeVisibility);
        Assert.True(result.HasGeometry);
        Assert.Equal(result.Background, result.StrokeBrush);
    }

    [Fact]
    public async Task Template_PerWidgetOnIconColour_WinsOverTheThemeDefault()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = CreateViewModel(
                initialOn: true,
                icon: "power",
                onIcon: "star",
                onIconColor: "#FF00FF00");
            var presenter = Realize(viewModel);
            return (presenter.Foreground as SolidColorBrush)?.Color;
        });

        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0x00), result);
    }

    [Fact]
    public async Task Template_PerWidgetAccentOverride_DoesNotColourTheOnIcon()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = CreateViewModel(
                initialOn: true,
                icon: "power",
                onIcon: "star",
                accent: "#FF00FF00");
            var presenter = Realize(viewModel);
            return new
            {
                Foreground = (presenter.Foreground as SolidColorBrush)?.Color,
                Background = ThemeColor(BackgroundKey),
            };
        });

        Assert.Equal(result.Background, result.Foreground);
        Assert.NotEqual(Color.FromRgb(0x00, 0xFF, 0x00), result.Foreground);
    }

    [Theory]
    [InlineData(ThemeIds.Dark)]
    [InlineData(ThemeIds.Light)]
    [InlineData(ThemeIds.HighContrast)]
    [InlineData(ThemeIds.Midnight)]
    [InlineData(ThemeIds.Nord)]
    [InlineData(ThemeIds.Oled)]
    public async Task DefaultOnIconColour_HasAtLeastFourPointFiveContrastAgainstTheActiveAccent(string themeId)
    {
        using var sta = new StaThread();

        var contrast = await sta.InvokeAsync(() =>
        {
            var theme = Load($"/TouchPanel;component/Theme/Themes/{ThemeDictionaryName(themeId)}");
            var background = ((SolidColorBrush)theme[BackgroundKey]).Color;
            var accent = ((SolidColorBrush)theme[AccentKey]).Color;
            return Contrast(background, accent);
        });

        Assert.True(
            contrast >= 4.5,
            $"Theme '{themeId}' default on-icon contrast against the active accent is {contrast:0.00}:1; expected at least 4.5:1.");
    }

    private static ToggleWidgetViewModel CreateViewModel(
        bool initialOn,
        string icon,
        string? onIcon,
        string? accent = null,
        string? onIconColor = null) => new(new ToggleWidgetConfig
    {
        Id = "toggle-1",
        X = 0,
        Y = 0,
        Width = 160,
        Height = 60,
        Label = "Mic",
        Icon = icon,
        OnIcon = onIcon,
        OnIconColor = onIconColor,
        InitialOn = initialOn,
        Accent = accent,
    });

    private static WidgetIconPresenter Realize(ToggleWidgetViewModel viewModel)
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(Load("/TouchPanel;component/Theme/Themes/Dark.xaml"));

        var template = Assert.IsType<DataTemplate>(
            Load("/TouchPanel;component/Widgets/ToggleWidget/ToggleWidgetTemplates.xaml")["Widget.Toggle"]);

        var presenter = new ContentPresenter
        {
            Content = viewModel,
            ContentTemplate = template,
        };

        var host = new Grid { Resources = resources };
        host.Children.Add(presenter);
        host.Measure(new Size(160, 60));
        host.Arrange(new Rect(0, 0, 160, 60));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.UpdateLayout();

        return Assert.IsType<WidgetIconPresenter>(
            FindDescendant<WidgetIconPresenter>(presenter, element => element.Name == "IconGlyph"));
    }

    private static string ThemeDictionaryName(string themeId) => ThemeIds.Normalize(themeId) switch
    {
        ThemeIds.Light => "Light.xaml",
        ThemeIds.HighContrast => "HighContrast.xaml",
        ThemeIds.Midnight => "Midnight.xaml",
        ThemeIds.Nord => "Nord.xaml",
        ThemeIds.Oled => "Oled.xaml",
        _ => "Dark.xaml",
    };

    private static Color ThemeColor(string key) =>
        ((SolidColorBrush)Load("/TouchPanel;component/Theme/Themes/Dark.xaml")[key]).Color;

    private static double Contrast(Color first, Color second)
    {
        var lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var scaled = value / 255.0;
            return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static ResourceDictionary Load(string componentUri) =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(new Uri(componentUri, UriKind.Relative)));

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T match && predicate(match))
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindDescendant(VisualTreeHelper.GetChild(root, i), predicate) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
