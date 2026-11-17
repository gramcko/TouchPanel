using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// Presentation of one page's optional background (user-requested addition): a solid
/// <see cref="Brush"/> for the configured <c>#RRGGBB</c>/<c>#AARRGGBB</c> color and a frozen,
/// bounded <see cref="BitmapSource"/> for the configured absolute image path. The host binds these
/// to layers behind the widgets; an unset or unusable value stays <see langword="null"/>, so the
/// theme background (the window's panel brush) shows through exactly as before.
/// </summary>
/// <remarks>
/// <para><b>Bounded and cached.</b> Images are decoded at most
/// <see cref="MaxDecodeDimension"/> pixels on the longest side and cached per path plus file
/// stamp, so switching back to a page does not decode the same file again. A file changed in place
/// gets a fresh entry because the cache key includes the length and last-write time.</para>
/// <para><b>Failure containment.</b> A relative, missing, or undecodable file never throws: the
/// image is simply unavailable and the color (or theme) still renders. Validation normally clears
/// such paths first, which keeps this a defensive second line.</para>
/// </remarks>
public sealed class PageBackgroundViewModel
{
    /// <summary>Largest decoded dimension in pixels for a page background image.</summary>
    public const int MaxDecodeDimension = PageBackground.MaxDecodeDimension;

    private static readonly ConcurrentDictionary<string, BitmapSource> ImageCache =
        new(StringComparer.OrdinalIgnoreCase);

    private PageBackgroundViewModel(string? color, string? imagePath, Brush? colorBrush, BitmapSource? image)
    {
        Color = color;
        ImagePath = imagePath;
        ColorBrush = colorBrush;
        Image = image;
    }

    /// <summary>Gets an empty background (theme only); shared because it is immutable.</summary>
    public static PageBackgroundViewModel Empty { get; } = new(color: null, imagePath: null, colorBrush: null, image: null);

    /// <summary>Gets the normalized color text, or <see langword="null"/> when unset.</summary>
    public string? Color { get; }

    /// <summary>Gets the absolute image path, or <see langword="null"/> when unset or unusable.</summary>
    public string? ImagePath { get; }

    /// <summary>Gets the frozen solid brush for <see cref="Color"/>, or <see langword="null"/> when unset or invalid.</summary>
    public Brush? ColorBrush { get; }

    /// <summary>Gets the frozen background image, or <see langword="null"/> when unset or undecodable.</summary>
    public BitmapSource? Image { get; }

    /// <summary>Gets a value indicating whether a solid background color was applied.</summary>
    public bool HasColor => ColorBrush is not null;

    /// <summary>Gets a value indicating whether a background image was applied.</summary>
    public bool HasImage => Image is not null;

    /// <summary>Gets a value indicating whether neither a color nor an image is applied.</summary>
    public bool IsEmpty => !HasColor && !HasImage;

    /// <summary>Gets the fit mode the host binds: the image fills the page without distortion.</summary>
    public Stretch ImageStretch => Stretch.UniformToFill;

    /// <summary>
    /// Builds the background presentation for one page. Never throws: invalid color/path values
    /// simply leave the corresponding layer empty.
    /// </summary>
    /// <param name="page">The validated page config; must not be <see langword="null"/>.</param>
    /// <returns>The background presentation; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page"/> is <see langword="null"/>.</exception>
    public static PageBackgroundViewModel Create(PageConfig page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Create(page.Background, page.BackgroundImage);
    }

    /// <summary>
    /// Builds the background presentation from raw color/path values (used by the page view model
    /// and by edit-time previews). Never throws.
    /// </summary>
    /// <param name="color">The color text, or <see langword="null"/> for none.</param>
    /// <param name="imagePath">The absolute image path, or <see langword="null"/> for none.</param>
    /// <returns>The background presentation; never <see langword="null"/>.</returns>
    public static PageBackgroundViewModel Create(string? color, string? imagePath)
    {
        var normalizedColor = WidgetAppearance.TryNormalizeColor(color, out var candidate) ? candidate : null;
        var brush = CreateBrush(normalizedColor);

        var path = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath.Trim();
        var image = path is null ? null : LoadCached(path);
        if (image is null)
        {
            path = null;
        }

        if (brush is null && image is null)
        {
            return Empty;
        }

        return new PageBackgroundViewModel(normalizedColor, path, brush, image);
    }

    /// <summary>
    /// Clears the decoded-image cache. Intended for deterministic tests; production relies on the
    /// path-plus-stamp keys so an edited file is re-decoded automatically.
    /// </summary>
    internal static void ClearImageCacheForTests() => ImageCache.Clear();

    private static Brush? CreateBrush(string? color)
    {
        if (color is null)
        {
            return null;
        }

        try
        {
            if (ColorConverter.ConvertFromString(color) is not Color parsed)
            {
                return null;
            }

            var brush = new SolidColorBrush(parsed);
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static BitmapSource? LoadCached(string path)
    {
        var key = CacheKey(path);
        if (key is null)
        {
            return null;
        }

        if (ImageCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = BoundedImageLoader.TryLoad(path, MaxDecodeDimension);
        if (image is not null)
        {
            ImageCache[key] = image;
        }

        return image;
    }

    private static string? CacheKey(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
