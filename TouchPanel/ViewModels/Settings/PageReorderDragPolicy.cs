namespace TouchPanel.ViewModels.Settings;

/// <summary>
/// One page row's vertical extent in the page list, relative to the list's top edge. The drag policy
/// only needs top/height pairs, so it stays free of WPF types and can be unit tested without a UI.
/// </summary>
/// <param name="Top">The row's top offset in DIPs, relative to the list.</param>
/// <param name="Height">The row's height in DIPs (including any row margin).</param>
public readonly record struct PageRowExtent(double Top, double Height);

/// <summary>
/// Pure drag-to-reorder decisions for the touch pages list: when a press becomes a drag, which
/// insertion slot the pointer is over, and which list index that slot means for the dragged row.
/// </summary>
/// <remarks>
/// The rules are intentionally small and deterministic so the view's pointer wiring stays thin and
/// both pages surfaces (Settings and edit mode) share exactly the same behavior. A drag starts only
/// after a deliberate vertical travel, so a tap still selects/opens a row and incidental horizontal
/// drift never reorders. The insertion index is measured between row midpoints, matching the caret
/// the view draws; converting it to a target index subtracts one when the row moves down because the
/// dragged row is still counted in the list while the pointer is over it.
/// </remarks>
public static class PageReorderDragPolicy
{
    /// <summary>
    /// Default vertical travel (in DIPs) required before a press becomes a reorder drag. It is large
    /// enough to keep taps and small jitter from reordering, and small enough for a finger.
    /// </summary>
    public const double DefaultDragThreshold = 12d;

    /// <summary>
    /// Reports whether pointer travel from the press origin should start a reorder drag: the vertical
    /// travel must reach <paramref name="threshold"/> and must dominate the horizontal travel.
    /// </summary>
    /// <param name="deltaX">Horizontal travel from the press origin, in DIPs.</param>
    /// <param name="deltaY">Vertical travel from the press origin, in DIPs.</param>
    /// <param name="threshold">The required vertical travel; non-positive values use the default.</param>
    /// <returns><see langword="true"/> when the gesture is a deliberate vertical drag.</returns>
    public static bool ShouldStartDrag(
        double deltaX,
        double deltaY,
        double threshold = DefaultDragThreshold)
    {
        if (double.IsNaN(deltaX) || double.IsNaN(deltaY)
            || double.IsInfinity(deltaX) || double.IsInfinity(deltaY))
        {
            return false;
        }

        var effectiveThreshold = threshold > 0 ? threshold : DefaultDragThreshold;
        var vertical = Math.Abs(deltaY);
        return vertical >= effectiveThreshold && vertical >= Math.Abs(deltaX);
    }

    /// <summary>
    /// Resolves the insertion slot for a pointer position: the index of the first row whose midpoint
    /// is below the pointer, or the row count when the pointer is below every midpoint. Slot 0 means
    /// "before the first row" and slot <c>rows.Count</c> means "after the last row".
    /// </summary>
    /// <param name="rows">The row extents in visual order; must not be <see langword="null"/>.</param>
    /// <param name="pointerY">The pointer's vertical position relative to the list's top edge, in DIPs.</param>
    /// <returns>The insertion slot in the range 0..<c>rows.Count</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rows"/> is <see langword="null"/>.</exception>
    public static int ResolveInsertionIndex(IReadOnlyList<PageRowExtent> rows, double pointerY)
    {
        ArgumentNullException.ThrowIfNull(rows);

        for (var index = 0; index < rows.Count; index++)
        {
            var midpoint = rows[index].Top + (rows[index].Height / 2d);
            if (pointerY < midpoint)
            {
                return index;
            }
        }

        return rows.Count;
    }

    /// <summary>
    /// Converts an insertion slot into the final 0-based index for the dragged row. Dragging down
    /// subtracts one because the dragged row still occupies its original slot while the pointer is
    /// below it; the result is clamped so a drop never leaves the list.
    /// </summary>
    /// <param name="sourceIndex">The dragged row's current index.</param>
    /// <param name="insertionIndex">The slot resolved by <see cref="ResolveInsertionIndex"/>.</param>
    /// <param name="itemCount">The number of rows in the list.</param>
    /// <returns>The destination index, or -1 when the list is empty or the source is out of range.</returns>
    public static int ResolveTargetIndex(int sourceIndex, int insertionIndex, int itemCount)
    {
        if (itemCount <= 0 || sourceIndex < 0 || sourceIndex >= itemCount)
        {
            return -1;
        }

        var slot = Math.Clamp(insertionIndex, 0, itemCount);
        var destination = slot > sourceIndex ? slot - 1 : slot;
        return Math.Clamp(destination, 0, itemCount - 1);
    }
}
