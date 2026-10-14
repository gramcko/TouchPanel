using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TouchPanel.ViewModels;

/// <summary>
/// Picks the effective foreground for a widget surface (RF-043/M2-RF-436): the configured
/// foreground override when one is set, otherwise the fallback supplied by the templated parent
/// (which carries the theme default and the disabled-state tone). Returning the parent value when
/// no override exists keeps the disabled foreground behavior intact.
/// </summary>
public sealed class AppearanceForegroundConverter : IMultiValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is Brush overrideBrush)
        {
            return overrideBrush;
        }

        if (values.Length > 1 && values[1] is Brush fallback)
        {
            return fallback;
        }

        return DependencyProperty.UnsetValue;
    }

    /// <inheritdoc/>
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
