using System.Globalization;

namespace TouchPanel.Config;

/// <summary>
/// Pure, WPF-free helpers and bounds for the per-widget appearance overrides layered over the theme
/// defaults (RF-043/M2-RF-436). Colors are carried as <c>#RRGGBB</c> or <c>#AARRGGBB</c> strings so
/// the configuration stays plain data; the view layer converts them to brushes.
/// </summary>
public static class WidgetAppearance
{
    /// <summary>Smallest accepted font size override, in DIPs (M2-RF-436).</summary>
    public const double MinFontSize = 8;

    /// <summary>Largest accepted font size override, in DIPs (M2-RF-436).</summary>
    public const double MaxFontSize = 96;

    /// <summary>Lowest accepted opacity override (M2-RF-436).</summary>
    public const double MinOpacity = 0.0;

    /// <summary>Highest accepted opacity override (M2-RF-436).</summary>
    public const double MaxOpacity = 1.0;

    /// <summary>
    /// Returns whether <paramref name="text"/> is a valid color override: <c>#RRGGBB</c> or
    /// <c>#AARRGGBB</c> (case-insensitive), with a leading <c>#</c>. A <see langword="null"/> or
    /// empty value is "unset", not invalid.
    /// </summary>
    /// <param name="text">The candidate color text.</param>
    /// <returns><see langword="true"/> when the value is unset or a valid color.</returns>
    public static bool IsValidColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return TryNormalizeColor(text, out _);
    }

    /// <summary>
    /// Normalizes a color override to uppercase <c>#RRGGBB</c>/<c>#AARRGGBB</c>. A <see langword="null"/>
    /// or blank value normalizes to <see langword="null"/> (theme default); an unparseable value also
    /// returns <see langword="null"/> so validation can clear it with a warning.
    /// </summary>
    /// <param name="text">The candidate color text.</param>
    /// <returns>The normalized color text, or <see langword="null"/> when unset or invalid.</returns>
    public static string? NormalizeColor(string? text) =>
        TryNormalizeColor(text, out var normalized) ? normalized : null;

    /// <summary>
    /// Attempts to normalize a color override. Unlike <see cref="NormalizeColor"/>, this
    /// distinguishes a blank (unset) value from an invalid one: blank fails to parse and yields
    /// <see langword="false"/> with a <see langword="null"/> result.
    /// </summary>
    /// <param name="text">The candidate color text.</param>
    /// <param name="normalized">The normalized color when the value parsed.</param>
    /// <returns><see langword="true"/> when the value is a valid color.</returns>
    public static bool TryNormalizeColor(string? text, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.Length is not (7 or 9) || value[0] != '#')
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

        normalized = "#" + value[1..].ToUpperInvariant();
        return true;
    }

    /// <summary>
    /// Normalizes a font-size override into <see cref="MinFontSize"/>..<see cref="MaxFontSize"/>;
    /// a <see langword="null"/> or non-finite value normalizes to <see langword="null"/> (theme
    /// default). A finite value is clamped to the bound (M2-RF-436).
    /// </summary>
    /// <param name="value">The candidate font size in DIPs.</param>
    /// <returns>The clamped font size, or <see langword="null"/> when unset.</returns>
    public static double? NormalizeFontSize(double? value)
    {
        if (value is not double finite || !double.IsFinite(finite))
        {
            return null;
        }

        return Math.Clamp(finite, MinFontSize, MaxFontSize);
    }

    /// <summary>
    /// Normalizes an opacity override into <see cref="MinOpacity"/>..<see cref="MaxOpacity"/>; a
    /// <see langword="null"/> or non-finite value normalizes to <see langword="null"/> (theme
    /// default). A finite value is clamped to the bound (M2-RF-436).
    /// </summary>
    /// <param name="value">The candidate opacity.</param>
    /// <returns>The clamped opacity, or <see langword="null"/> when unset.</returns>
    public static double? NormalizeOpacity(double? value)
    {
        if (value is not double finite || !double.IsFinite(finite))
        {
            return null;
        }

        return Math.Clamp(finite, MinOpacity, MaxOpacity);
    }

    /// <summary>Formats a nullable font size for display using the invariant culture.</summary>
    /// <param name="value">The font size in DIPs.</param>
    /// <returns>The formatted value.</returns>
    public static string FormatFontSize(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Formats a nullable opacity for display using the invariant culture.</summary>
    /// <param name="value">The opacity in <c>[0,1]</c>.</param>
    /// <returns>The formatted value.</returns>
    public static string FormatOpacity(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
