using System.Globalization;
using System.Windows.Media;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// Converts a configured <c>#RRGGBB</c>/<c>#AARRGGBB</c> appearance override into a frozen
/// <see cref="Brush"/> for the widget templates (RF-043/M2-RF-436). Returns <see langword="null"/>
/// when the value is unset, so templates fall back to the theme default via
/// <c>TargetNullValue</c>.
/// </summary>
internal static class AppearanceBrushFactory
{
    /// <summary>
    /// Creates a frozen brush for <paramref name="color"/>, or <see langword="null"/> when the value
    /// is unset or not a valid color (validation normalizes configuration, so invalid values are not
    /// expected here; the null fallback keeps the view resilient regardless).
    /// </summary>
    /// <param name="color">The color override text.</param>
    /// <returns>A frozen brush, or <see langword="null"/> for the theme default.</returns>
    public static Brush? Create(string? color)
    {
        if (!WidgetAppearance.TryNormalizeColor(color, out var normalized) || normalized is null)
        {
            return null;
        }

        var hex = normalized.AsSpan(1);
        byte alpha;
        byte red;
        byte green;
        byte blue;
        if (hex.Length == 8)
        {
            alpha = ParseByte(hex[..2]);
            red = ParseByte(hex.Slice(2, 2));
            green = ParseByte(hex.Slice(4, 2));
            blue = ParseByte(hex.Slice(6, 2));
        }
        else
        {
            alpha = 0xFF;
            red = ParseByte(hex[..2]);
            green = ParseByte(hex.Slice(2, 2));
            blue = ParseByte(hex.Slice(4, 2));
        }

        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static byte ParseByte(ReadOnlySpan<char> hex) =>
        byte.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
