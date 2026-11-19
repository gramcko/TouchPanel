using System.Globalization;
using System.Windows.Data;
using TouchPanel.Config;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Marks the tile of the inline icon picker that matches the picker's current selection
/// (user-requested replacement for the long icon dropdowns). The tile style evaluates this
/// multi-binding against the tile's <see cref="IconOption"/> item and the owning
/// <see cref="InlineIconPicker.SelectedItem"/>; matching is by the stable catalog id so a
/// re-created option record still marks the same entry.
/// </summary>
public sealed class IconOptionSelectedConverter : IMultiValueConverter
{
    /// <summary>Returns whether the tile item is the picker's selected entry.</summary>
    /// <param name="values">The tile item followed by the picker's selected item.</param>
    /// <param name="targetType">The binding target type (unused).</param>
    /// <param name="parameter">The converter parameter (unused).</param>
    /// <param name="culture">The binding culture (unused).</param>
    /// <returns><see langword="true"/> when both values are icon options with the same id.</returns>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is [IconOption item, IconOption selected, ..])
        {
            return string.Equals(item.Id, selected.Id, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Not supported: the converter only drives one-way selection styling.</summary>
    /// <param name="value">The binding value (unused).</param>
    /// <param name="targetTypes">The binding target types (unused).</param>
    /// <param name="parameter">The converter parameter (unused).</param>
    /// <param name="culture">The binding culture (unused).</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
