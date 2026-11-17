using System.IO;

namespace TouchPanel.Config;

/// <summary>
/// Pure, WPF-free helpers and bounds for the optional per-page background (user-requested
/// addition). Colors use the same <c>#RRGGBB</c>/<c>#AARRGGBB</c> shape as the widget appearance
/// overrides, and the background image must be an absolute path to an existing raster file that
/// carries a recognized image signature. Keeping the checks here lets validation and the pages
/// editor share one rule set without pulling imaging types into the configuration layer.
/// </summary>
public static class PageBackground
{
    /// <summary>
    /// Largest decoded dimension in pixels for the background image (user-requested bound). A larger
    /// source is decoded down to this on its longest side so the in-memory bitmap stays bounded.
    /// </summary>
    public const int MaxDecodeDimension = 2048;

    /// <summary>
    /// Returns whether <paramref name="path"/> is a usable background image: an absolute path to an
    /// existing file whose header matches a raster format the view layer can decode. A
    /// <see langword="null"/> or blank value is "unset", not invalid.
    /// </summary>
    /// <param name="path">The candidate image path.</param>
    /// <returns><see langword="true"/> when the value is unset or a usable image path.</returns>
    public static bool IsUsableImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return TryNormalizeImagePath(path, out _);
    }

    /// <summary>
    /// Normalizes a background image path. A blank value yields <see langword="null"/> (no image);
    /// a relative, missing, unreadable, or non-image file fails and yields
    /// <see langword="false"/> so validation can clear it with a warning.
    /// </summary>
    /// <param name="path">The candidate image path.</param>
    /// <param name="normalized">The trimmed absolute path when the value is usable; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the value is blank or a usable image path.</returns>
    public static bool TryNormalizeImagePath(string? path, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var trimmed = path.Trim();
        if (!IsAbsolutePath(trimmed) || !File.Exists(trimmed))
        {
            return false;
        }

        if (!HasRecognizedSignature(trimmed))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    private static bool IsAbsolutePath(string path)
    {
        try
        {
            return Path.IsPathRooted(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Cheap header probe so validation rejects a clearly non-image file without decoding pixels
    /// (which would need the WPF imaging stack). The renderer still contains a decode failure, so a
    /// corrupt file with a valid header degrades to the theme background instead of crashing.
    /// </summary>
    private static bool HasRecognizedSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        int read;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            read = stream.Read(header);
        }
        catch (Exception)
        {
            return false;
        }

        if (read >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return true; // PNG
        }

        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return true; // JPEG
        }

        if (read >= 6
            && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46
            && header[3] == 0x38 && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
        {
            return true; // GIF87a / GIF89a
        }

        if (read >= 2 && header[0] == 0x42 && header[1] == 0x4D)
        {
            return true; // BMP
        }

        if (read >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return true; // WebP (RIFF....WEBP)
        }

        if (read >= 4
            && ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
                || (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)))
        {
            return true; // TIFF
        }

        return false;
    }
}
