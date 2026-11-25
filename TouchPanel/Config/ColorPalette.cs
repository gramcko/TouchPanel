namespace TouchPanel.Config;

/// <summary>
/// One touch-sized colour choice offered by the inline colour picker (user-requested appearance
/// editor): a normalized <c>#RRGGBB</c>/<c>#AARRGGBB</c> value plus a human-readable name. The
/// palette is plain, WPF-free data so both the property editor and the pages editor share it.
/// </summary>
/// <param name="Hex">The normalized colour text.</param>
/// <param name="Name">The display name shown as the tile tooltip.</param>
public sealed record ColorSwatch(string Hex, string Name);

/// <summary>
/// The fixed, touch-friendly colour palette used by the inline colour picker (user-requested
/// replacement for plain hex entry). The order is intentional: neutrals and theme-ish darks first,
/// then the accent blues and the vivid hues, six per row inside the 300 DIP property panel.
/// </summary>
public static class ColorPalette
{
    /// <summary>Number of swatch tiles per row (the panel is 300 DIP wide).</summary>
    public const int Columns = 6;

    /// <summary>The touch target of one swatch tile, in DIPs.</summary>
    public const double SwatchDips = 40;

    /// <summary>
    /// The built-in palette, in display order. Exactly <see cref="Columns"/> entries per row.
    /// </summary>
    public static IReadOnlyList<ColorSwatch> Standard { get; } =
    [
        new("#000000", "Black"),
        new("#FFFFFF", "White"),
        new("#1B1B1F", "Panel dark"),
        new("#0E1620", "Deep navy"),
        new("#2A2A30", "Graphite"),
        new("#3F3F49", "Slate"),
        new("#6E6E78", "Gray"),
        new("#A0A0A8", "Light gray"),
        new("#C5CAD3", "Silver"),
        new("#EDEDED", "Off-white"),
        new("#3D7EFF", "Accent blue"),
        new("#1D5FE0", "Deep blue"),
        new("#FF4D4D", "Red"),
        new("#FF8A3D", "Orange"),
        new("#FFB000", "Amber"),
        new("#FFE14D", "Yellow"),
        new("#4CD964", "Green"),
        new("#2FA36B", "Emerald"),
        new("#14B8A6", "Teal"),
        new("#00C2FF", "Cyan"),
        new("#5B5BFF", "Indigo"),
        new("#9B5BFF", "Purple"),
        new("#FF66C4", "Pink"),
        new("#E91E63", "Magenta"),
    ];

    /// <summary>
    /// Parses the picker's hex input, accepting <c>#RGB</c>, <c>#RRGGBB</c>, and
    /// <c>#AARRGGBB</c> (case-insensitive). A three-digit value expands each nibble to a byte
    /// (<c>#ABC</c> becomes <c>#AABBCC</c>); the result is always uppercase. Blank or malformed
    /// text is rejected.
    /// </summary>
    /// <param name="text">The candidate colour text.</param>
    /// <param name="normalized">The normalized colour when the value parsed; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is a complete colour value.</returns>
    public static bool TryParseHex(string? text, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.Length is not (4 or 7 or 9) || value[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        var hex = value[1..];
        if (hex.Length == 3)
        {
            hex = string.Create(6, hex, static (span, source) =>
            {
                span[0] = source[0];
                span[1] = source[0];
                span[2] = source[1];
                span[3] = source[1];
                span[4] = source[2];
                span[5] = source[2];
            });
        }

        normalized = "#" + hex.ToUpperInvariant();
        return true;
    }

    /// <summary>
    /// Returns whether <paramref name="text"/> is an acceptable in-progress hex entry: an optional
    /// leading <c>#</c> followed by at most eight hex digits. This is the keystroke filter, so
    /// partial values are allowed while only a complete value can be committed.
    /// </summary>
    /// <param name="text">The candidate partial text.</param>
    /// <returns><see langword="true"/> when the text can be kept in the input.</returns>
    public static bool IsAllowedHexInput(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (text[0] != '#' || text.Length > 9)
        {
            return false;
        }

        for (var index = 1; index < text.Length; index++)
        {
            if (!Uri.IsHexDigit(text[index]))
            {
                return false;
            }
        }

        return true;
    }
}
