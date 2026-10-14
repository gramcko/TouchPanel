namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// Pure grid-snapping math for edit mode (RF-044/M2-RF-441/M2-RF-442/M2-RF-446, T-15). It rounds a
/// coordinate to the nearest multiple of the configured grid size while keeping the result finite:
/// hostile/non-finite inputs and out-of-range grid sizes are normalized rather than propagated. No
/// clamping to the page or the 40 DIP minimum happens here — that composition stays in
/// <see cref="EditModeService"/> so the order (snap, then clamp, then minimum) is explicit and
/// unit-tested.
/// </summary>
public static class GridSnap
{
    /// <summary>Default edit grid size in DIPs (M2-RF-441).</summary>
    public const double DefaultGridSize = 8;

    /// <summary>Smallest accepted grid size in DIPs (M2-RF-441).</summary>
    public const double MinGridSize = 2;

    /// <summary>Largest accepted grid size in DIPs (M2-RF-441).</summary>
    public const double MaxGridSize = 64;

    /// <summary>
    /// Normalizes a configured grid size into the supported <c>[2, 64]</c> DIP range (M2-RF-441).
    /// Non-finite values fall back to <see cref="DefaultGridSize"/> so a hostile configuration can
    /// never produce a zero or infinite step.
    /// </summary>
    /// <param name="gridSize">The requested grid size in DIPs.</param>
    /// <returns>A finite grid size within <c>[2, 64]</c>.</returns>
    public static double NormalizeGridSize(double gridSize) =>
        double.IsFinite(gridSize) ? Math.Clamp(gridSize, MinGridSize, MaxGridSize) : DefaultGridSize;

    /// <summary>
    /// Rounds <paramref name="value"/> to the nearest multiple of the normalized
    /// <paramref name="gridSize"/>. Midpoints round away from zero so the result is deterministic
    /// across platforms; a non-finite value degrades to 0 instead of propagating.
    /// </summary>
    /// <param name="value">The coordinate to snap in DIPs.</param>
    /// <param name="gridSize">The requested grid size in DIPs; normalized before use.</param>
    /// <returns>The nearest grid multiple, always finite.</returns>
    public static double Snap(double value, double gridSize)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        var grid = NormalizeGridSize(gridSize);
        return Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid;
    }

    /// <summary>
    /// Snaps an extent (width/height) to the grid unless it is already smaller than one grid cell
    /// (M2-RF-442): a widget smaller than a grid cell must not have its size collapsed, so only its
    /// origin snaps and the extent passes through to the clamp/minimum pipeline.
    /// </summary>
    /// <param name="extent">The requested width or height in DIPs.</param>
    /// <param name="gridSize">The requested grid size in DIPs; normalized before use.</param>
    /// <returns>The snapped extent, or the normalized input when it is smaller than a grid cell.</returns>
    public static double SnapExtent(double extent, double gridSize)
    {
        var grid = NormalizeGridSize(gridSize);
        if (!double.IsFinite(extent) || extent < grid)
        {
            return double.IsFinite(extent) ? extent : 0;
        }

        return Snap(extent, grid);
    }
}
