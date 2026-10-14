using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TouchPanel.ViewModels;

/// <summary>
/// Resolves the translucent fill brush of the Sparkline "filled" style (M4-RF-950/D-M4-4): it picks
/// the effective accent with the same precedence as the line (per-widget <c>AccentBrush</c> override
/// first, otherwise the theme accent supplied by the templated parent's <c>Tag</c> via
/// <c>{DynamicResource PanelAccentBrush}</c>) and applies the documented fill alpha
/// (<see cref="DefaultFillAlpha"/>). Returning a fresh brush from the multi-binding keeps realized
/// elements following a live theme/accent swap (M3-RF-851) while the per-widget override still wins.
/// </summary>
public sealed class AppearanceAccentFillConverter : IMultiValueConverter
{
    /// <summary>
    /// The documented translucency applied to the effective accent for the filled area (the value
    /// the template used before M4, now derived rather than hard-coded).
    /// </summary>
    public const byte DefaultFillAlpha = 0x55;

    /// <inheritdoc/>
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Precedence mirrors the line: a per-widget accent override wins over the theme fallback.
        Brush? effective = null;
        if (values.Length > 0 && values[0] is Brush overrideBrush)
        {
            effective = overrideBrush;
        }
        else if (values.Length > 1 && values[1] is Brush fallback)
        {
            effective = fallback;
        }

        if (effective is null)
        {
            // No accent available (for example the palette key is unset): let the plot use its
            // neutral default fill rather than throwing.
            return DependencyProperty.UnsetValue;
        }

        if (effective is not SolidColorBrush solid)
        {
            // Only solid accents carry an extractable channel set; leave any other brush untouched.
            return effective;
        }

        var color = solid.Color;
        var translucent = new SolidColorBrush(Color.FromArgb(DefaultFillAlpha, color.R, color.G, color.B));
        translucent.Freeze();
        return translucent;
    }

    /// <inheritdoc/>
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
