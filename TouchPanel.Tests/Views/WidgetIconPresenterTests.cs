using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Views.Controls;

namespace TouchPanel.Tests.Views;

/// <summary>
/// The reusable widget icon presenter (user-requested brand icons): glyph ids render the MDL2
/// TextBlock, vector ids render the Path at ~82% of the glyph box with the widget foreground, and
/// both stay vertically centred in the same square box so surrounding layouts are unchanged.
/// </summary>
public sealed class WidgetIconPresenterTests
{
    [Fact]
    public async Task VectorIcon_RendersASizedPathAndHidesTheGlyph()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var presenter = new WidgetIconPresenter
            {
                PathData = IconCatalog.PathDataFor("steam"),
                IconSize = 40,
                Foreground = Brushes.Red,
            };

            var host = new Grid { Width = 60, Height = 60 };
            host.Children.Add(presenter);
            host.Measure(new Size(60, 60));
            host.Arrange(new Rect(0, 0, 60, 60));
            host.UpdateLayout();
            presenter.UpdateLayout();

            var box = Assert.IsType<Grid>(presenter.FindName("IconBox"));
            var glyph = Assert.IsType<TextBlock>(presenter.FindName("GlyphText"));
            var path = Assert.IsType<Path>(presenter.FindName("VectorPath"));

            return (
                BoxWidth: box.Width,
                BoxHeight: box.Height,
                GlyphVisibility: glyph.Visibility,
                PathVisibility: path.Visibility,
                PathWidth: path.Width,
                PathHeight: path.Height,
                PathStretch: path.Stretch,
                PathHasGeometry: path.Data is not null,
                PathFill: (path.Fill as SolidColorBrush)?.Color,
                PathVerticalAlignment: path.VerticalAlignment);
        });

        Assert.Equal(40, result.BoxWidth);
        Assert.Equal(40, result.BoxHeight);
        Assert.Equal(Visibility.Collapsed, result.GlyphVisibility);
        Assert.Equal(Visibility.Visible, result.PathVisibility);
        Assert.Equal(40 * WidgetIconPresenter.VectorScale, result.PathWidth, precision: 3);
        Assert.Equal(40 * WidgetIconPresenter.VectorScale, result.PathHeight, precision: 3);
        Assert.Equal(Stretch.Uniform, result.PathStretch);
        Assert.True(result.PathHasGeometry);
        Assert.Equal(Colors.Red, result.PathFill);
        Assert.Equal(VerticalAlignment.Center, result.PathVerticalAlignment);
    }

    [Fact]
    public async Task GlyphIcon_RendersTheMdl2TextBlockAndHidesThePath()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var presenter = new WidgetIconPresenter
            {
                Glyph = IconCatalog.GlyphFor("power"),
                IconSize = 18,
            };

            var host = new Grid { Width = 40, Height = 40 };
            host.Children.Add(presenter);
            host.Measure(new Size(40, 40));
            host.Arrange(new Rect(0, 0, 40, 40));
            host.UpdateLayout();
            presenter.UpdateLayout();

            var box = Assert.IsType<Grid>(presenter.FindName("IconBox"));
            var glyph = Assert.IsType<TextBlock>(presenter.FindName("GlyphText"));
            var path = Assert.IsType<Path>(presenter.FindName("VectorPath"));

            return (
                BoxWidth: box.Width,
                GlyphVisibility: glyph.Visibility,
                GlyphText: glyph.Text,
                GlyphFont: glyph.FontFamily.Source,
                GlyphFontSize: glyph.FontSize,
                PathVisibility: path.Visibility);
        });

        Assert.Equal(18, result.BoxWidth);
        Assert.Equal(Visibility.Visible, result.GlyphVisibility);
        Assert.Equal(IconCatalog.GlyphFor("power"), result.GlyphText);
        Assert.Equal("Segoe MDL2 Assets", result.GlyphFont);
        Assert.Equal(18, result.GlyphFontSize);
        Assert.Equal(Visibility.Collapsed, result.PathVisibility);
    }

    [Fact]
    public async Task Presenter_FallsBackToTheDefaultBoxForAHostileIconSize()
    {
        using var sta = new StaThread();

        var width = await sta.InvokeAsync(() =>
        {
            var presenter = new WidgetIconPresenter
            {
                Glyph = IconCatalog.GlyphFor("star"),
                IconSize = double.NaN,
            };

            presenter.Measure(new Size(40, 40));
            presenter.Arrange(new Rect(0, 0, 40, 40));
            presenter.UpdateLayout();
            return ((Grid)presenter.FindName("IconBox")).Width;
        });

        Assert.Equal(20, width);
    }
}
