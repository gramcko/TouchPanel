using System.Collections.Generic;
using System.Linq;

namespace TouchPanel.Config;

/// <summary>
/// Read/write seam for the user's most-recently-used colour list (user-requested colour picker).
/// Widget appearance fields and the page-background picker share one instance, so a colour picked
/// anywhere moves to the front everywhere.
/// </summary>
public interface IRecentColorPalette
{
    /// <summary>Gets the current palette, most recent first (never empty; fixed maximum size).</summary>
    IReadOnlyList<ColorSwatch> Colors { get; }

    /// <summary>Records a colour as the most recent (normalized, de-duplicated, oldest dropped).</summary>
    /// <param name="hex">The colour text; blank or malformed values are ignored.</param>
    void Record(string? hex);
}

/// <summary>
/// In-memory most-recently-used colour list (user-requested colour picker). The list keeps at most
/// <see cref="Capacity"/> entries: recording a colour moves it to the front (case-insensitive
/// de-duplication) and drops the oldest entry when the list is full. A missing, empty, or wholly
/// invalid list falls back to <see cref="ColorPalette.Standard"/> so the picker always has choices.
/// </summary>
/// <remarks>
/// The type is WPF-free and owns the MRU rules; <see cref="RecentColorPersistence"/> adds the
/// debounced <c>state.json</c> write on top of it. The in-memory instance used by tests/design time
/// never touches the file system.
/// </remarks>
public class RecentColorPalette : IRecentColorPalette
{
    /// <summary>The fixed palette size: 24 swatches, matching the default palette.</summary>
    public const int Capacity = 24;

    private readonly List<string> _colors;

    /// <summary>Initializes a palette from the supplied seed, falling back to the defaults.</summary>
    /// <param name="seed">The persisted colour list, or <see langword="null"/> for the defaults.</param>
    public RecentColorPalette(IEnumerable<string>? seed = null) => _colors = Normalize(seed);

    /// <inheritdoc />
    public IReadOnlyList<ColorSwatch> Colors => _colors
        .Select(hex => new ColorSwatch(hex, NameFor(hex)))
        .ToArray();

    /// <summary>Gets the current colours as normalized hex strings, most recent first.</summary>
    public IReadOnlyList<string> HexColors => _colors.ToArray();

    /// <summary>
    /// Normalizes an arbitrary persisted/live colour list: values are parsed to canonical
    /// uppercase hex, de-duplicated case-insensitively (first occurrence wins), truncated to
    /// <see cref="Capacity"/>, and replaced with the defaults when nothing valid remains.
    /// </summary>
    /// <param name="values">The candidate colour list.</param>
    /// <returns>A fresh, mutable normalized list.</returns>
    public static List<string> Normalize(IEnumerable<string>? values)
    {
        var result = new List<string>(Capacity);
        if (values is not null)
        {
            foreach (var value in values)
            {
                if (!ColorPalette.TryParseHex(value, out var normalized) || normalized is null)
                {
                    continue;
                }

                if (result.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(normalized);
                if (result.Count == Capacity)
                {
                    break;
                }
            }
        }

        if (result.Count == 0)
        {
            result.AddRange(ColorPalette.Standard.Select(swatch => swatch.Hex));
        }

        return result;
    }

    /// <summary>Gets the default colour list (the standard palette order), as hex strings.</summary>
    public static IReadOnlyList<string> Defaults { get; } =
        ColorPalette.Standard.Select(swatch => swatch.Hex).ToArray();

    /// <inheritdoc />
    public void Record(string? hex)
    {
        if (!ColorPalette.TryParseHex(hex, out var normalized) || normalized is null)
        {
            return;
        }

        _colors.RemoveAll(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
        _colors.Insert(0, normalized);
        if (_colors.Count > Capacity)
        {
            _colors.RemoveRange(Capacity, _colors.Count - Capacity);
        }

        OnChanged();
    }

    /// <summary>Replaces the palette contents with a normalized copy of the supplied list.</summary>
    /// <param name="values">The new colour list (defaults are used when nothing valid remains).</param>
    protected void Replace(IEnumerable<string>? values)
    {
        _colors.Clear();
        _colors.AddRange(Normalize(values));
        OnChanged();
    }

    /// <summary>Gets the mutable backing list (used by the persistence adapter to take snapshots).</summary>
    protected IReadOnlyList<string> Snapshot => _colors;

    /// <summary>Raises <see cref="Changed"/> after a mutation.</summary>
    protected virtual void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised after the list changes, so an open picker can re-order its tiles.</summary>
    public event EventHandler? Changed;

    private static string NameFor(string hex) =>
        ColorPalette.Standard.FirstOrDefault(swatch =>
            string.Equals(swatch.Hex, hex, StringComparison.OrdinalIgnoreCase))?.Name ?? hex;
}
