using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Writes a real PNG for tests that need an existing, decodable image (Image widget, page
/// background). The file is generated with WPF's encoder so decoding exercises the production path.
/// </summary>
internal static class TestPng
{
    public static string Write(string directory, string fileName, int width = 32, int height = 32)
    {
        var path = Path.Combine(directory, fileName);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x20;
            pixels[i + 1] = 0x60;
            pixels[i + 2] = 0xA0;
            pixels[i + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
        return path;
    }
}
