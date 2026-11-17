using System.IO;
using System.Windows.Media.Imaging;

namespace TouchPanel.ViewModels;

/// <summary>
/// Shared bounded image decode used by the Image widget and the per-page background (user-requested
/// addition). The source file is opened read-only, decoded at most <c>maxDimension</c> pixels on its
/// longest side (a cheap metadata probe decides whether to down-scale), frozen, and released with
/// <see cref="BitmapCacheOption.OnLoad"/>, so a very large or hostile file cannot exhaust memory and
/// no file handle stays open. A failure returns <see langword="null"/> instead of throwing.
/// </summary>
internal static class BoundedImageLoader
{
    /// <summary>
    /// Attempts to decode <paramref name="path"/> bounded to <paramref name="maxDimension"/>.
    /// </summary>
    /// <param name="path">The absolute image path.</param>
    /// <param name="maxDimension">The largest accepted decoded dimension in pixels.</param>
    /// <returns>The frozen bitmap, or <see langword="null"/> when the file cannot be decoded.</returns>
    public static BitmapSource? TryLoad(string path, int maxDimension)
    {
        if (maxDimension <= 0)
        {
            return null;
        }

        try
        {
            var image = Load(path, maxDimension);
            return image.PixelWidth > 0 && image.PixelHeight > 0 ? image : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes <paramref name="path"/> bounded to <paramref name="maxDimension"/> pixels on its
    /// longest side. A cheap metadata probe (no pixel decode) decides whether to down-scale; when
    /// the probe cannot report dimensions, the decode is still bounded to the cap.
    /// </summary>
    /// <param name="path">The absolute image path.</param>
    /// <param name="maxDimension">The largest accepted decoded dimension in pixels.</param>
    /// <returns>The frozen decoded bitmap.</returns>
    /// <exception cref="Exception">Propagates the decoder's exception; callers that need containment use <see cref="TryLoad"/>.</exception>
    public static BitmapSource Load(string path, int maxDimension)
    {
        var (pixelWidth, pixelHeight) = ProbeDimensions(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;

        if (pixelWidth > 0 && pixelHeight > 0)
        {
            if (pixelWidth > maxDimension || pixelHeight > maxDimension)
            {
                if (pixelWidth >= pixelHeight)
                {
                    image.DecodePixelWidth = maxDimension;
                }
                else
                {
                    image.DecodePixelHeight = maxDimension;
                }
            }
        }
        else
        {
            // The probe could not report dimensions; bound the decode unconditionally.
            image.DecodePixelWidth = maxDimension;
        }

        image.EndInit();
        image.Freeze();
        return image;
    }

    private static (int Width, int Height) ProbeDimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception)
        {
            // Some formats may not expose dimensions during a delayed probe; the caller still bounds.
            return (0, 0);
        }
    }
}
