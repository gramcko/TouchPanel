using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Views.Controls;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Real-BAML coverage for the user-requested toggle on-state icon: the toggle template binds the icon
/// presenter to the display-icon properties, so the glyph/vector swaps with <c>IsOn</c>, and the
/// presenter foreground resolves to the theme accent (or the per-widget accent override) only while
/// the toggle is on with an on icon configured.
/// </summary>
public sealed class ToggleOnIconTemplateTests
{
    private const string AccentKey = "PanelAccentBrush";
    private const string ForegroundKey = "PanelForegroundBrush";

    [Fact]
    public async Task Template_OnStateWithGlyphOnIcon_ShowsTheOnGlyphInTheThemeAccent()
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
                Accent = ThemeColor(AccentKey),
            };
        });

        Assert.Equal(IconCatalog.GlyphFor("star"), result.GlyphText);
        Assert.Equal(result.Accent, result.Foreground);
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
                Accent = ThemeColor(AccentKey),
            };
        });

        Assert.Equal(IconCatalog.GlyphFor("power"), result.GlyphText);
        Assert.Equal(result.PanelForeground, result.Foreground);
        Assert.NotEqual(result.Accent, result.Foreground);
    }

    [Fact]
    public async Task Template_OnStateWithVectorOnIcon_ShowsAStrokedAccentPath()
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
                Accent = ThemeColor(AccentKey),
            };
        });

        Assert.Equal(Visibility.Collapsed, result.GlyphVisibility);
        Assert.Equal(Visibility.Collapsed, result.FilledVisibility);
        Assert.Equal(Visibility.Visible, result.StrokeVisibility);
        Assert.True(result.HasGeometry);
        Assert.Equal(result.Accent, result.StrokeBrush);
    }

    [Fact]
    public async Task Template_PerWidgetAccentOverride_WinsForTheOnIcon()
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
            return (presenter.Foreground as SolidColorBrush)?.Color;
        });

        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0x00), result);
    }

    private static ToggleWidgetViewModel CreateViewModel(
        bool initialOn,
        string icon,
        string? onIcon,
        string? accent = null) => new(new ToggleWidgetConfig
    {
        Id = "toggle-1",
        X = 0,
        Y = 0,
        Width = 160,
        Height = 60,
        Label = "Mic",
        Icon = icon,
        OnIcon = onIcon,
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

    private static Color ThemeColor(string key) =>
        ((SolidColorBrush)Load("/TouchPanel;component/Theme/Themes/Dark.xaml")[key]).Color;

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
