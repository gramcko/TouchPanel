using System.Globalization;
using System.Windows.Data;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Sizes the docked picker flyout to the space left of the 300 DIP property panel (user-requested
/// docked flyout): roughly 380 DIP at desktop widths, shrinking to the available slot so it is never
/// clipped at the 512x300 minimum panel size.
/// </summary>
public sealed class FlyoutWidthConverter : IValueConverter
{
    /// <summary>The preferred flyout width at desktop sizes.</summary>
    public const double PreferredWidth = 380;

    /// <summary>The property panel width plus its right margin and the gap to the flyout.</summary>
    public const double ReservedWidth = 330;

    /// <summary>The narrowest width still usable for a single tile column.</summary>
    public const double MinimumWidth = 120;

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var available = value is double width && double.IsFinite(width) ? width : 0;
        return Math.Clamp(available - ReservedWidth, MinimumWidth, PreferredWidth);
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
