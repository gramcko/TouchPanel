using System.Windows;
using System.Windows.Media.Imaging;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Asserts that a window declared its icon through the compiled BAML. The XAML sets
/// <c>Icon="/TouchPanel;component/Assets/touchpanel.ico"</c>; WPF resolves that pack URI while
/// loading the BAML, so a non-null frame backed by an <see cref="IconBitmapDecoder"/> proves both
/// the property and the packaged resource are present.
/// </summary>
internal static class WindowIconAssert
{
    internal static void IsPackagedAppIcon(Window window)
    {
        var frame = Assert.IsAssignableFrom<BitmapFrame>(window.Icon);
        Assert.IsType<IconBitmapDecoder>(frame.Decoder);
        Assert.True(frame.PixelWidth > 0);
        Assert.Equal(frame.PixelWidth, frame.PixelHeight);
    }
}
