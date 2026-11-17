using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Views;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Per-page background rendering in the real <see cref="WidgetPageHost"/> BAML (user-requested
/// addition): the color and image layers render behind the widgets, are never hit-testable or
/// focusable, the widget surface stays above them, the theme shows through when unset, and swapping
/// the bound page (page switch/edit/reorder path) updates the layers. No window or hardware.
/// </summary>
public sealed class PageBackgroundHostTests
{
    public PageBackgroundHostTests() => PageBackgroundViewModel.ClearImageCacheForTests();

    [Fact]
    public async Task ColorAndImage_RenderBehindTheHitTestableWidgets()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "wall.png", 64, 32);
            var page = Page(config => config.Pages[0].BackgroundImage = path, background: "#FF102030");
            var host = new WidgetPageHost { DataContext = page };
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var root = Assert.IsType<Grid>(host.Content);
            var color = host.ColorBackgroundForTest;
            var image = host.ImageBackgroundForTest;
            var widgets = host.WidgetItemsForTest;

            return new
            {
                Children = root.Children.Cast<UIElement>().ToArray(),
                Color = color,
                Image = image,
                Widgets = widgets,
                ColorHitTest = color.IsHitTestVisible,
                ImageHitTest = image.IsHitTestVisible,
                ColorFocusable = color.Focusable,
                ImageFocusable = image.Focusable,
                WidgetsHitTest = widgets.IsHitTestVisible,
                Brush = color.Background as SolidColorBrush,
                Source = image.Source,
                Stretch = image.Stretch,
                WidgetCount = widgets.Items.Count,
            };
        });

        Assert.Equal(3, result.Children.Length);
        Assert.Same(result.Color, result.Children[0]);
        Assert.Same(result.Image, result.Children[1]);
        Assert.Same(result.Widgets, result.Children[2]);
        Assert.False(result.ColorHitTest);
        Assert.False(result.ImageHitTest);
        Assert.False(result.ColorFocusable);
        Assert.False(result.ImageFocusable);
        Assert.True(result.WidgetsHitTest);
        Assert.Equal(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), Assert.IsType<SolidColorBrush>(result.Brush).Color);
        Assert.IsAssignableFrom<BitmapSource>(result.Source);
        Assert.Equal(Stretch.UniformToFill, result.Stretch);
        Assert.Equal(1, result.WidgetCount);
    }

    [Fact]
    public async Task UnsetBackground_LeavesTheThemeVisible()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var page = Page(_ => { });
            var host = new WidgetPageHost { DataContext = page };
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var root = Assert.IsType<Grid>(host.Content);
            return new
            {
                RootBackground = root.Background,
                ColorBackground = host.ColorBackgroundForTest.Background,
                ImageSource = host.ImageBackgroundForTest.Source,
            };
        });

        Assert.Equal(Brushes.Transparent, result.RootBackground);
        Assert.Null(result.ColorBackground);
        Assert.Null(result.ImageSource);
    }

    [Fact]
    public async Task SwappingThePage_UpdatesTheBackgroundLayers()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var first = Page(_ => { }, background: "#FF112233");
            var second = Page(_ => { }, background: "#FF445566");
            var host = new WidgetPageHost { DataContext = first };
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var firstColor = ((SolidColorBrush)host.ColorBackgroundForTest.Background!).Color;

            host.DataContext = second;
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var secondColor = ((SolidColorBrush)host.ColorBackgroundForTest.Background!).Color;
            var imageAfterSwap = host.ImageBackgroundForTest.Source;
            var widgets = host.WidgetItemsForTest.Items.Count;

            return (firstColor, secondColor, imageAfterSwap, widgets);
        });

        Assert.Equal(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), result.firstColor);
        Assert.Equal(Color.FromArgb(0xFF, 0x44, 0x55, 0x66), result.secondColor);
        Assert.Null(result.imageAfterSwap);
        Assert.Equal(1, result.widgets);
    }

    private static PageViewModel Page(Action<PanelConfig> configure, string? background = null)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Background = background,
                    Widgets = [new LabelWidgetConfig { Id = "l", X = 4, Y = 4, Width = 120, Height = 40, Label = "Label" }],
                },
            ],
        };
        configure(config);
        return PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
    }
}
