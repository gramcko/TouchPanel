using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Theme;

namespace TouchPanel.Tests.Theme;

/// <summary>
/// User-requested app-wide scrollbar theming. Loads the real <c>Theme/Styles.xaml</c> (BAML) plus a
/// real theme dictionary on an STA thread, realizes a real <see cref="ScrollViewer"/> with content
/// that overflows both axes, and asserts:
/// <list type="bullet">
/// <item>the implicit <see cref="ScrollBar"/>/<see cref="ScrollViewer"/> styles are applied to every
/// scrollbar the ScrollViewer creates, vertically and horizontally;</item>
/// <item>the template parts exist and the theme brushes resolve (no missing palette keys) in every
/// built-in theme;</item>
/// <item>there are no arrow buttons, the paging commands match the orientation, and the touch-sized
/// thumb keeps a minimum grab length;</item>
/// <item>a live theme swap (dark → light/highContrast) re-resolves the realized track and thumb
/// brushes through the same DynamicResource path the application uses.</item>
/// </list>
/// </summary>
public sealed class ScrollBarStyleTests
{
    [Theory]
    [InlineData(ThemeIds.Dark)]
    [InlineData(ThemeIds.Light)]
    [InlineData(ThemeIds.HighContrast)]
    [InlineData(ThemeIds.Midnight)]
    [InlineData(ThemeIds.Nord)]
    [InlineData(ThemeIds.Oled)]
    public async Task ScrollViewer_AppliesImplicitThemeScrollBars_InBothOrientations(string themeId)
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var theme = LoadTheme(themeId);
            var styles = LoadStyles();
            var host = new Grid();
            host.Resources.MergedDictionaries.Add(theme);
            host.Resources.MergedDictionaries.Add(styles);

            var (viewer, vertical, horizontal) = RealizeScrollViewer(host);

            // The implicit app-wide styles are the effective styles of the ScrollViewer and of the
            // ScrollBars its own template creates (no per-instance styling anywhere).
            Assert.Same(styles[typeof(ScrollViewer)], viewer.Style);
            Assert.Same(styles[typeof(ScrollBar)], vertical.Style);
            Assert.Same(styles[typeof(ScrollBar)], horizontal.Style);

            // Touch-friendly 14 DIP strip along the scrolling axis.
            Assert.Equal(14, vertical.ActualWidth, 1);
            Assert.Equal(14, horizontal.ActualHeight, 1);

            // Vertical: paging commands, a synced track, and the theme brushes resolved.
            AssertTrackAndThumb(
                theme,
                vertical,
                Orientation.Vertical,
                ScrollBar.PageUpCommand,
                ScrollBar.PageDownCommand,
                minimumGrabLength: 24);

            // Horizontal: the orientation trigger switches the template and its commands.
            AssertTrackAndThumb(
                theme,
                horizontal,
                Orientation.Horizontal,
                ScrollBar.PageLeftCommand,
                ScrollBar.PageRightCommand,
                minimumGrabLength: 24);
        });
    }

    [Fact]
    public async Task ScrollViewer_CornerChrome_BlendsWithThePanel()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var host = new Grid();
            host.Resources.MergedDictionaries.Add(LoadTheme(ThemeIds.Dark));
            host.Resources.MergedDictionaries.Add(LoadStyles());

            var (_, vertical, horizontal) = RealizeScrollViewer(host);

            // The default template's bottom-right corner (the only Rectangle in the ScrollViewer
            // chrome) must not paint the stock light system brush over the themed panel.
            foreach (var rectangle in FindDescendants<System.Windows.Shapes.Rectangle>(host))
            {
                Assert.True(
                    rectangle.Fill is not SolidColorBrush { Color.A: > 0 },
                    "The ScrollViewer corner chrome must stay transparent so the panel shows through.");
            }

            // Sanity: both scrollbars are the themed ones, so the only chrome left is the corner.
            Assert.NotNull(RequireThumb(vertical));
            Assert.NotNull(RequireThumb(horizontal));
        });
    }

    [Theory]
    [InlineData(ThemeIds.Light)]
    [InlineData(ThemeIds.HighContrast)]
    public async Task ThemeSwap_ReResolvesRealizedScrollBarBrushes(string targetThemeId)
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, _) = TestLoggerFactory.Create();
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(LoadTheme(ThemeIds.Dark));
            resources.MergedDictionaries.Add(LoadStyles());
            var manager = new ThemeManager(logger, resources);

            var host = new Grid { Resources = resources };
            var (_, vertical, horizontal) = RealizeScrollViewer(host);
            var thumb = RequireThumb(vertical);
            var track = RequireTemplateBorder(vertical, "TrackSurface");

            var dark = LoadTheme(ThemeIds.Dark);
            Assert.Equal(ColorOf(dark, "PanelDisabledForegroundBrush"), BrushColor(thumb.Background));
            Assert.Equal(ColorOf(dark, "WidgetDisabledBrush"), BrushColor(track.Background));
            Assert.Equal(ColorOf(dark, "WidgetBorderBrush"), BrushColor(track.BorderBrush));

            // The real ThemeManager swap path used by the application settings.
            Assert.True(manager.Apply(targetThemeId));
            host.UpdateLayout();

            var next = LoadTheme(targetThemeId);
            Assert.Equal(ColorOf(next, "PanelDisabledForegroundBrush"), BrushColor(thumb.Background));
            Assert.Equal(ColorOf(next, "WidgetDisabledBrush"), BrushColor(track.Background));
            Assert.Equal(ColorOf(next, "WidgetBorderBrush"), BrushColor(track.BorderBrush));

            // The other axis follows the same swap.
            Assert.Equal(
                ColorOf(next, "PanelDisabledForegroundBrush"),
                BrushColor(RequireThumb(horizontal).Background));

            Assert.NotEqual(
                ColorOf(dark, "PanelDisabledForegroundBrush"),
                BrushColor(thumb.Background));
        });
    }

    private static void AssertTrackAndThumb(
        ResourceDictionary theme,
        ScrollBar bar,
        Orientation orientation,
        RoutedCommand decreaseCommand,
        RoutedCommand increaseCommand,
        double minimumGrabLength)
    {
        Assert.Equal(orientation, bar.Orientation);

        var track = Assert.IsType<Track>(bar.Template.FindName("PART_Track", bar));
        Assert.Equal(orientation, track.Orientation);
        Assert.Equal(bar.Minimum, track.Minimum, 3);
        Assert.Equal(bar.Maximum, track.Maximum, 3);
        Assert.Equal(bar.ViewportSize, track.ViewportSize, 3);
        Assert.True(track.Maximum > 0, "The scrollbar should report scrollable content.");

        // No arrow (line) buttons: the only repeat buttons are the two paging areas.
        var repeatButtons = FindDescendants<RepeatButton>(bar);
        Assert.Equal(2, repeatButtons.Count);
        Assert.Contains(repeatButtons, b => ReferenceEquals(b.Command, decreaseCommand));
        Assert.Contains(repeatButtons, b => ReferenceEquals(b.Command, increaseCommand));

        // Track and thumb use the palette through DynamicResource, so every theme resolves them.
        var trackSurface = RequireTemplateBorder(bar, "TrackSurface");
        Assert.Equal(ColorOf(theme, "WidgetDisabledBrush"), BrushColor(trackSurface.Background));
        Assert.Equal(ColorOf(theme, "WidgetBorderBrush"), BrushColor(trackSurface.BorderBrush));

        var thumb = RequireThumb(bar);
        Assert.Equal(ColorOf(theme, "PanelDisabledForegroundBrush"), BrushColor(thumb.Background));

        // The visible thumb is laid out and never shrinks below the touch grab length.
        var grabLength = orientation == Orientation.Vertical ? thumb.ActualHeight : thumb.ActualWidth;
        Assert.True(
            grabLength >= minimumGrabLength - 0.5,
            $"Expected a {minimumGrabLength} DIP minimum thumb, got {grabLength}.");
    }

    private static (ScrollViewer Viewer, ScrollBar Vertical, ScrollBar Horizontal) RealizeScrollViewer(Grid host)
    {
        var viewer = new ScrollViewer
        {
            Width = 220,
            Height = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // Content larger than the viewport in both axes, so both scrollbars are visible.
        viewer.Content = new Border { Width = 800, Height = 800 };

        host.Children.Add(viewer);
        host.Measure(new Size(220, 120));
        host.Arrange(new Rect(0, 0, 220, 120));
        host.UpdateLayout();

        Assert.Equal(Visibility.Visible, viewer.ComputedVerticalScrollBarVisibility);
        Assert.Equal(Visibility.Visible, viewer.ComputedHorizontalScrollBarVisibility);

        var vertical = Assert.Single(FindDescendants<ScrollBar>(viewer), b => b.Orientation == Orientation.Vertical);
        var horizontal = Assert.Single(FindDescendants<ScrollBar>(viewer), b => b.Orientation == Orientation.Horizontal);

        vertical.ApplyTemplate();
        horizontal.ApplyTemplate();
        return (viewer, vertical, horizontal);
    }

    private static Thumb RequireThumb(ScrollBar bar) =>
        Assert.Single(FindDescendants<Thumb>(bar));

    private static Border RequireTemplateBorder(ScrollBar bar, string name) =>
        Assert.IsType<Border>(bar.Template.FindName(name, bar));

    private static Color BrushColor(Brush? brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;

    private static Color ColorOf(ResourceDictionary dictionary, string key) =>
        Assert.IsType<SolidColorBrush>(dictionary[key]).Color;

    private static List<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new List<T>();
        Walk(root, found);
        return found;
    }

    private static void Walk<T>(DependencyObject node, List<T> found)
        where T : DependencyObject
    {
        if (node is T match)
        {
            found.Add(match);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), found);
        }
    }

    private static ResourceDictionary LoadStyles() =>
        Load("/TouchPanel;component/Theme/Styles.xaml");

    private static ResourceDictionary LoadTheme(string themeId)
    {
        Assert.True(ThemeIds.TryGetResourceUri(themeId, out var uri));
        return Assert.IsType<ResourceDictionary>(Application.LoadComponent(uri));
    }

    private static ResourceDictionary Load(string componentUri) =>
        Assert.IsType<ResourceDictionary>(
            Application.LoadComponent(new Uri(componentUri, UriKind.Relative)));
}
