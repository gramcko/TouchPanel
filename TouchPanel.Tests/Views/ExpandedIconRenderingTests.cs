using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Views.Controls;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Rendered smoke test for the expanded catalog: a sheet containing every new glyph, filled vector,
/// and stroked line-art icon is rasterized through <see cref="WidgetIconPresenter"/> and
/// <see cref="RenderTargetBitmap"/>; every cell must contain visible pixels, which catches broken or
/// invisible paths that a geometry-existence check alone would miss.
/// </summary>
public sealed class ExpandedIconRenderingTests
{
    private static readonly string[] NewIds =
    [
        "headset", "volumeLow", "volumeMedium", "volumeHigh", "microphoneOff",
        "brightness", "fullscreen", "restoreWindow", "sleep", "restart", "unlock", "key",
        "shield", "ethernet", "usb", "hardDrive", "print", "screenshot",
        "undo", "redo", "copy", "paste", "cut",
        "zoomIn", "zoomOut", "filter", "sort",
        "calculator", "code", "chat", "people", "phone", "videoCall", "photos", "cart",
        "headphones", "headphonesMuted", "microphoneMuted",
    ];

    [Mdl2FontFact]
    public async Task EveryNewIcon_RendersVisiblePixelsOnTheSmokeSheet()
    {
        using var sta = new StaThread();

        var coverage = await sta.InvokeAsync(() =>
        {
            const int columns = 7;
            const int cell = 44;
            const double iconSize = 30;
            var rows = (NewIds.Length + columns - 1) / columns;

            var sheet = new UniformGrid
            {
                Columns = columns,
                Rows = rows,
                Width = columns * cell,
                Height = rows * cell,
                Background = Brushes.Transparent,
            };

            foreach (var id in NewIds)
            {
                Assert.True(
                    WidgetIconResolver.TryResolve(IconPackIds.Default, id, out var visual),
                    $"Icon '{id}' did not resolve.");
                sheet.Children.Add(new WidgetIconPresenter
                {
                    Glyph = visual.Glyph,
                    PathData = visual.PathData,
                    Stroke = visual.Stroke,
                    StrokeUnits = visual.StrokeUnits,
                    IconSize = iconSize,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            sheet.Measure(new Size(sheet.Width, sheet.Height));
            sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height));
            sheet.UpdateLayout();

            var width = columns * cell;
            var height = rows * cell;
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(sheet);

            var stride = width * 4;
            var pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            var counts = new int[NewIds.Length];
            for (var index = 0; index < NewIds.Length; index++)
            {
                var column = index % columns;
                var row = index / columns;
                var left = column * cell + 3;
                var top = row * cell + 3;
                var right = left + cell - 6;
                var bottom = top + cell - 6;

                var visible = 0;
                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        if (pixels[y * stride + x * 4 + 3] > 8)
                        {
                            visible++;
                        }
                    }
                }

                counts[index] = visible;
            }

            return counts;
        });

        for (var index = 0; index < NewIds.Length; index++)
        {
            Assert.True(
                coverage[index] > 5,
                $"Icon '{NewIds[index]}' rendered only {coverage[index]} visible pixels on the smoke sheet.");
        }
    }
}
