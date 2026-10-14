using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Serilog.Events;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Theme;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Theme;

/// <summary>
/// M3-RF-843/M3-RF-845/M3-RF-848 real-BAML coverage for the global accent: <see cref="ThemeManager"/>
/// layers an accent brush over the active theme, restores the theme accent when cleared, falls back
/// on an invalid value, and rolls back a failed apply wholesale. It also proves the precedence order
/// (global theme &lt; global accent &lt; per-widget override) on realized widgets.
/// </summary>
public sealed class ThemeManagerAccentTests
{
    [Fact]
    public async Task ApplyAccent_OverridesTheThemeAccentBrush()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var host = HostWithDark();
            var manager = new ThemeManager(logger, host);

            Assert.True(manager.Apply(ThemeIds.Dark, "#112233"));

            var brush = Assert.IsType<SolidColorBrush>(host[AccentKey]);
            Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), brush.Color);
            Assert.Equal("#112233", manager.CurrentAccentColor);
        });
    }

    [Fact]
    public async Task ClearAccent_RestoresTheThemeAccent()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var host = HostWithDark();
            var manager = new ThemeManager(logger, host);

            Assert.True(manager.Apply(ThemeIds.Dark, "#112233"));
            Assert.True(host.Contains(AccentKey));

            Assert.True(manager.Apply(ThemeIds.Dark, null));

            Assert.Null(manager.CurrentAccentColor);
            Assert.Equal(ThemeAccentColor(), ResolvedAccentColor(host));
        });
    }

    [Fact]
    public async Task InvalidAccent_FallsBackToTheThemeAccentWithAWarning()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, sink) = TestLoggerFactory.Create();
            var host = HostWithDark();
            var manager = new ThemeManager(logger, host);

            Assert.True(manager.Apply(ThemeIds.Dark, "not-a-color"));

            Assert.Null(manager.CurrentAccentColor);
            Assert.Equal(ThemeAccentColor(), ResolvedAccentColor(host));
            Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("accent", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public async Task ApplyAccentWithThemeSwap_IsAtomic_AndIdempotent()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var host = HostWithDark();
            var manager = new ThemeManager(logger, host);

            Assert.True(manager.Apply(ThemeIds.Light, "#445566"));

            var light = Assert.Single(host.MergedDictionaries);
            Assert.Equal(ThemeIds.Light, light[ThemeIds.ThemeIdKey]);
            Assert.Equal(Color.FromRgb(0x44, 0x55, 0x66), Assert.IsType<SolidColorBrush>(host[AccentKey]).Color);

            // Re-applying the same theme+accent is a no-op.
            Assert.True(manager.Apply(ThemeIds.Light, "#445566"));
            Assert.Single(host.MergedDictionaries);
        });
    }

    [Fact]
    public async Task AccentApplyFailure_RestoresPreviousThemeAndAccentWholesale()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, sink) = TestLoggerFactory.Create();
            var dark = Load("/TouchPanel;component/Theme/Themes/Dark.xaml");
            var host = new ResourceDictionary();
            host.MergedDictionaries.Add(dark);

            var manager = new ThemeManager(
                logger,
                host,
                themeLoader: null,
                swap: null,
                accentApplier: (_, _) => throw new InvalidOperationException("injected accent failure"));

            Assert.False(manager.Apply(ThemeIds.Light, "#112233"));

            // Wholesale rollback: the dark theme dictionary is restored and no accent override remains.
            Assert.Same(dark, Assert.Single(host.MergedDictionaries));
            Assert.Equal(ThemeIds.Dark, manager.CurrentThemeId);
            Assert.Null(manager.CurrentAccentColor);
            Assert.Equal(ThemeAccentColor(), ResolvedAccentColor(host));
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Error
                    && e.Exception is InvalidOperationException { Message: "injected accent failure" });
        });
    }

    [Fact]
    public async Task PerWidgetAccentOverride_WinsOverGlobalAccent_AndNonOverriddenFollowsIt()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var host = HostWithDark();
            var manager = new ThemeManager(logger, host);
            Assert.True(manager.Apply(ThemeIds.Dark, "#112233"));

            var template = LoadTemplate("/TouchPanel;component/Widgets/ToggleWidget/ToggleWidgetTemplates.xaml", "Widget.Toggle");

            using var plainVm = new ToggleWidgetViewModel(new ToggleWidgetConfig
            {
                Id = "plain",
                X = 0,
                Y = 0,
                Width = 160,
                Height = 60,
                Label = "Plain",
            });

            using var overriddenVm = new ToggleWidgetViewModel(new ToggleWidgetConfig
            {
                Id = "overridden",
                X = 0,
                Y = 0,
                Width = 160,
                Height = 60,
                Label = "Overridden",
                Accent = "#FF00FF00",
            });

            var grid = new Grid { Resources = host };
            var plainLayer = OnLayer(Realize(grid, template, plainVm));
            var overriddenLayer = OnLayer(Realize(grid, template, overriddenVm));

            Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), Assert.IsType<SolidColorBrush>(plainLayer.Background).Color);
            Assert.Equal(Color.FromRgb(0x00, 0xFF, 0x00), Assert.IsType<SolidColorBrush>(overriddenLayer.Background).Color);

            // A live global-accent change updates the non-overridden widget but not the override.
            Assert.True(manager.Apply(ThemeIds.Dark, "#0000FF"));
            grid.UpdateLayout();

            Assert.Equal(Color.FromRgb(0x00, 0x00, 0xFF), Assert.IsType<SolidColorBrush>(plainLayer.Background).Color);
            Assert.Equal(Color.FromRgb(0x00, 0xFF, 0x00), Assert.IsType<SolidColorBrush>(overriddenLayer.Background).Color);
        });
    }

    private const string AccentKey = "PanelAccentBrush";

    private static Color ResolvedAccentColor(ResourceDictionary host) =>
        Assert.IsType<SolidColorBrush>(host[AccentKey]).Color;

    private static Color ThemeAccentColor() =>
        Assert.IsType<SolidColorBrush>(Load("/TouchPanel;component/Theme/Themes/Dark.xaml")[AccentKey]).Color;

    private static ResourceDictionary HostWithDark()
    {
        var host = new ResourceDictionary();
        host.MergedDictionaries.Add(Load("/TouchPanel;component/Theme/Themes/Dark.xaml"));
        return host;
    }

    private static ContentPresenter Realize(Grid host, DataTemplate template, object content)
    {
        var presenter = new ContentPresenter
        {
            Content = content,
            ContentTemplate = template,
        };

        host.Children.Add(presenter);
        host.Measure(new Size(160, 60));
        host.Arrange(new Rect(0, 0, 160, 60));
        host.UpdateLayout();
        return presenter;
    }

    private static Border OnLayer(ContentPresenter root) =>
        Assert.IsType<Border>(FindDescendant<Border>(root, border => border.Tag is SolidColorBrush));

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

    private static DataTemplate LoadTemplate(string componentUri, string key)
    {
        var dictionary = Load(componentUri);
        return Assert.IsType<DataTemplate>(dictionary[key]);
    }

    private static ResourceDictionary Load(string componentUri) =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(new Uri(componentUri, UriKind.Relative)));
}
