using System.Globalization;
using System.Windows.Data;
using TouchPanel.Config;
using TouchPanel.ViewModels;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Marks the tile of the inline colour picker that matches the picker's current selection
/// (user-requested colour picker). Matching is by the normalized hex value; when the field is
/// inherited/unset, only the "None" tile is marked.
/// </summary>
public sealed class ColorSwatchSelectedConverter : IMultiValueConverter
{
    /// <summary>Returns whether the tile item is the picker's selected entry.</summary>
    /// <param name="values">The tile item, the picker's selected hex, and the inherited flag.</param>
    /// <param name="targetType">The binding target type (unused).</param>
    /// <param name="parameter">The converter parameter (unused).</param>
    /// <param name="culture">The binding culture (unused).</param>
    /// <returns><see langword="true"/> when the tile represents the current colour.</returns>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [ColorSwatchOption item, ..])
        {
            return false;
        }

        var selected = values.Length > 1 ? values[1] as string : null;
        var inherited = values.Length > 2 && values[2] is true;

        if (inherited || string.IsNullOrEmpty(selected))
        {
            return item.IsNone;
        }

        return !item.IsNone && string.Equals(item.Hex, selected, StringComparison.OrdinalIgnoreCase);
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

/// <summary>
/// One bindable tile of the inline colour picker: a palette colour or the "None" entry. The brush is
/// pre-frozen so the tile grid realizes without per-item conversion cost.
/// </summary>
public sealed class ColorSwatchOption
{
    /// <summary>Initializes a tile.</summary>
    /// <param name="hex">The normalized colour text, or an empty string for "None".</param>
    /// <param name="name">The display name (also the tile tooltip).</param>
    /// <param name="brush">The frozen swatch brush; transparent for "None".</param>
    /// <param name="isNone">Whether this is the optional "None" tile.</param>
    public ColorSwatchOption(string hex, string name, System.Windows.Media.Brush brush, bool isNone = false)
    {
        Hex = hex;
        Name = name;
        Brush = brush;
        IsNone = isNone;
    }

    /// <summary>Gets the normalized colour text, or an empty string for "None".</summary>
    public string Hex { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets the frozen swatch brush.</summary>
    public System.Windows.Media.Brush Brush { get; }

    /// <summary>Gets a value indicating whether this is the optional "None" tile.</summary>
    public bool IsNone { get; }

    /// <summary>Gets the text drawn on the tile (only "None" carries text; colours are bare swatches).</summary>
    public string DisplayText => IsNone ? "None" : string.Empty;

    /// <summary>Builds the tile list from the shared palette, optionally appending "None".</summary>
    /// <param name="allowNone">Whether the "None" tile is offered.</param>
    /// <returns>The tiles in display order.</returns>
    public static IReadOnlyList<ColorSwatchOption> CreateTiles(bool allowNone) =>
        CreateTiles(ColorPalette.Standard, allowNone);

    /// <summary>Builds the tile list from an explicit palette (for example the MRU-ordered one).</summary>
    /// <param name="swatches">The palette entries in display order.</param>
    /// <param name="allowNone">Whether the "None" tile is offered.</param>
    /// <returns>The tiles in display order.</returns>
    public static IReadOnlyList<ColorSwatchOption> CreateTiles(IEnumerable<ColorSwatch> swatches, bool allowNone)
    {
        var tiles = new List<ColorSwatchOption>(ColorPalette.Standard.Count + 1);
        foreach (var swatch in swatches)
        {
            tiles.Add(new ColorSwatchOption(
                swatch.Hex,
                swatch.Name,
                AppearanceBrushFactory.Create(swatch.Hex) ?? System.Windows.Media.Brushes.Transparent));
        }

        if (allowNone)
        {
            tiles.Add(new ColorSwatchOption(string.Empty, "None", System.Windows.Media.Brushes.Transparent, isNone: true));
        }

        return tiles;
    }
}
