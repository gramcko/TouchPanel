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
/// M3-RF-845/M3-RF-848/AC-M3-12 real-BAML coverage for <see cref="ThemeManager"/>: the manager
/// swaps the application-level theme dictionary, unknown ids fall back to dark with a warning, and
/// an injected swap failure restores the previous theme wholesale (no mixed palette keys) while a
/// realized widget follows the swap.
/// </summary>
public sealed class ThemeManagerTests
{
    [Fact]
    public async Task Apply_Light_ChangesRealizedWidgetBrush()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var dark = Load("/TouchPanel;component/Theme/Themes/Dark.xaml");
            var host = new ResourceDictionary();
            host.MergedDictionaries.Add(dark);
            var manager = new ThemeManager(logger, host);

            Assert.Equal(ThemeIds.Dark, manager.CurrentThemeId);

            var grid = new Grid { Resources = host };
            using var viewModel = CreateButtonViewModel();
            var button = RealizeButton(grid, viewModel);

            var darkSurface = SurfaceColor(Load("/TouchPanel;component/Theme/Colors.xaml"));
            Assert.Equal(darkSurface, BrushColor(button.Background));

            Assert.True(manager.Apply(ThemeIds.Light));
            grid.UpdateLayout();

            var light = Assert.Single(host.MergedDictionaries);
            Assert.Equal(ThemeIds.Light, light[ThemeIds.ThemeIdKey]);
            var lightSurface = SurfaceColor(light);
            Assert.NotEqual(darkSurface, lightSurface);
            Assert.Equal(lightSurface, BrushColor(button.Background));
            Assert.Equal(ThemeIds.Light, manager.CurrentThemeId);
        });
    }

    [Fact]
    public async Task Apply_UnknownId_FallsBackToDark_WithWarning()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, sink) = TestLoggerFactory.Create();
            var host = new ResourceDictionary();
            host.MergedDictionaries.Add(Load("/TouchPanel;component/Theme/Themes/Dark.xaml"));
            var manager = new ThemeManager(logger, host);

            Assert.True(manager.Apply(ThemeIds.Light));
            Assert.Equal(ThemeIds.Light, manager.CurrentThemeId);

            Assert.True(manager.Apply("not-a-real-theme"));

            Assert.Equal(ThemeIds.Dark, manager.CurrentThemeId);
            var active = Assert.Single(host.MergedDictionaries);
            Assert.Equal(ThemeIds.Dark, active[ThemeIds.ThemeIdKey]);

            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Warning
                    && e.RenderMessage().Contains("not-a-real-theme", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Apply_MissingId_FallsBackToDark()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var host = new ResourceDictionary();
            host.MergedDictionaries.Add(Load("/TouchPanel;component/Theme/Themes/Dark.xaml"));
            var manager = new ThemeManager(logger, host);

            Assert.True(manager.Apply(ThemeIds.Light));
            Assert.True(manager.Apply(null));

            Assert.Equal(ThemeIds.Dark, manager.CurrentThemeId);
            Assert.Equal(ThemeIds.Dark, Assert.Single(host.MergedDictionaries)[ThemeIds.ThemeIdKey]);
        });
    }

    [Fact]
    public async Task Apply_InjectedSwapFailure_RestoresPreviousThemeWholesale_AndLogs()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, sink) = TestLoggerFactory.Create();
            var dark = Load("/TouchPanel;component/Theme/Themes/Dark.xaml");
            var host = new ResourceDictionary();
            host.MergedDictionaries.Add(dark);

            // Simulates a partial application: the new theme is merged, then the swap fails.
            Action<ResourceDictionary, ResourceDictionary, ResourceDictionary?> failingSwap = (resources, next, previous) =>
            {
                var merged = resources.MergedDictionaries;
                if (previous is not null)
                {
                    merged.Remove(previous);
                }

                merged.Add(next);
                throw new InvalidOperationException("injected theme swap failure");
            };

            var manager = new ThemeManager(logger, host, themeLoader: null, swap: failingSwap);

            var grid = new Grid { Resources = host };
            using var viewModel = CreateButtonViewModel();
            var button = RealizeButton(grid, viewModel);
            var darkSurface = SurfaceColor(Load("/TouchPanel;component/Theme/Colors.xaml"));
            Assert.Equal(darkSurface, BrushColor(button.Background));

            Assert.False(manager.Apply(ThemeIds.Light));

            // Wholesale rollback: exactly the pre-swap dictionary state remains, no Light left behind.
            var restored = Assert.Single(host.MergedDictionaries);
            Assert.Same(dark, restored);
            Assert.Equal(ThemeIds.Dark, manager.CurrentThemeId);

            grid.UpdateLayout();
            Assert.Equal(darkSurface, BrushColor(button.Background));

            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Error
                    && e.Exception is InvalidOperationException { Message: "injected theme swap failure" });
        });
    }

    [Fact]
    public async Task Apply_IsIdempotent_AndSwapsOnce()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var host = new ResourceDictionary();
            host.MergedDictionaries.Add(Load("/TouchPanel;component/Theme/Themes/Dark.xaml"));

            var swapCount = 0;
            Action<ResourceDictionary, ResourceDictionary, ResourceDictionary?> countingSwap = (resources, next, previous) =>
            {
                swapCount++;
                var merged = resources.MergedDictionaries;
                if (previous is not null && merged.IndexOf(previous) is var index && index >= 0)
                {
                    merged[index] = next;
                    return;
                }

                merged.Add(next);
            };

            var manager = new ThemeManager(logger, host, themeLoader: null, swap: countingSwap);

            Assert.True(manager.Apply(ThemeIds.Light));
            Assert.True(manager.Apply(ThemeIds.Light));

            Assert.Equal(1, swapCount);
            Assert.Equal(ThemeIds.Light, manager.CurrentThemeId);
        });
    }

    [Fact]
    public async Task Apply_LoadFailure_KeepsCurrentTheme_AndLogs()
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
                themeLoader: _ => throw new InvalidOperationException("injected theme load failure"));

            Assert.False(manager.Apply(ThemeIds.Light));
            Assert.Equal(ThemeIds.Dark, manager.CurrentThemeId);
            Assert.Same(dark, Assert.Single(host.MergedDictionaries));
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Error
                    && e.Exception is InvalidOperationException { Message: "injected theme load failure" });
        });
    }

    [Fact]
    public async Task ThemeChanged_RaisedOnSuccessfulSwapOnly()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var host = new ResourceDictionary();
            host.MergedDictionaries.Add(Load("/TouchPanel;component/Theme/Themes/Dark.xaml"));
            var manager = new ThemeManager(logger, host);

            var changes = new List<ThemeChangedEventArgs>();
            manager.ThemeChanged += (_, args) => changes.Add(args);

            manager.Apply(ThemeIds.Light);
            manager.Apply(ThemeIds.Light); // no-op
            manager.Apply(ThemeIds.HighContrast);

            Assert.Equal(2, changes.Count);
            Assert.Equal(ThemeIds.Light, changes[0].ThemeId);
            Assert.Equal(ThemeIds.Dark, changes[0].PreviousThemeId);
            Assert.Equal(ThemeIds.HighContrast, changes[1].ThemeId);
            Assert.Equal(ThemeIds.Light, changes[1].PreviousThemeId);
        });
    }

    private static ButtonWidgetViewModel CreateButtonViewModel() =>
        new(new ButtonWidgetConfig
        {
            Id = "theme-button",
            X = 0,
            Y = 0,
            Width = 160,
            Height = 60,
            Label = "Go",
            Action = new MuteActionConfig(),
        });

    private static Button RealizeButton(Grid host, ButtonWidgetViewModel viewModel)
    {
        var templates = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
            new Uri("/TouchPanel;component/Widgets/ButtonWidget/ButtonWidgetTemplates.xaml", UriKind.Relative)));
        var template = Assert.IsType<DataTemplate>(templates["Widget.Button"]);

        var presenter = new ContentPresenter
        {
            Content = viewModel,
            ContentTemplate = template,
        };

        host.Children.Add(presenter);
        host.Measure(new Size(160, 60));
        host.Arrange(new Rect(0, 0, 160, 60));
        host.UpdateLayout();

        return Assert.IsType<Button>(FindDescendant<Button>(presenter));
    }

    private static Color SurfaceColor(ResourceDictionary dictionary) =>
        Assert.IsType<SolidColorBrush>(dictionary["WidgetSurfaceBrush"]).Color;

    private static Color BrushColor(Brush? brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindDescendant<T>(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static ResourceDictionary Load(string componentUri) =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(new Uri(componentUri, UriKind.Relative)));
}
