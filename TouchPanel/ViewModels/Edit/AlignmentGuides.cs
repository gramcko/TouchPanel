namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// One alignment guide produced while dragging in edit mode (RF-044/M2-RF-444, T-15). A guide is a
/// full-length line through the aligned coordinate; <see cref="IsVertical"/> decides which axis the
/// line runs along. The <c>X1/Y1/X2/Y2</c> members are directly bindable by the overlay's guide
/// layer and keep this type WPF-free so the geometry stays purely unit-testable (M2-RF-446).
/// </summary>
/// <param name="IsVertical"><see langword="true"/> for a vertical guide (constant X); <see langword="false"/> for a horizontal guide (constant Y).</param>
/// <param name="Position">The aligned X (vertical) or Y (horizontal) coordinate in DIPs.</param>
/// <param name="Start">The line start along the other axis in DIPs.</param>
/// <param name="End">The line end along the other axis in DIPs.</param>
public readonly record struct GuideLine(bool IsVertical, double Position, double Start, double End)
{
    /// <summary>Gets the X coordinate of the line's first end.</summary>
    public double X1 => IsVertical ? Position : Start;

    /// <summary>Gets the Y coordinate of the line's first end.</summary>
    public double Y1 => IsVertical ? Start : Position;

    /// <summary>Gets the X coordinate of the line's second end.</summary>
    public double X2 => IsVertical ? Position : End;

    /// <summary>Gets the Y coordinate of the line's second end.</summary>
    public double Y2 => IsVertical ? End : Position;
}

/// <summary>
/// Result of an alignment query (RF-044/M2-RF-444/M2-RF-446): the offset the moving widget should
/// shift by to sit flush on the nearest aligned edge/center, and the guides to draw. The offsets are
/// zero when nothing is within the threshold. The type is intentionally WPF-free.
/// </summary>
/// <param name="DeltaX">The horizontal offset to apply in DIPs (0 when not aligned).</param>
/// <param name="DeltaY">The vertical offset to apply in DIPs (0 when not aligned).</param>
/// <param name="Guides">The guides to render for the resolved alignments; may be empty.</param>
public readonly record struct AlignmentSolution(double DeltaX, double DeltaY, IReadOnlyList<GuideLine> Guides);

/// <summary>
/// Pure alignment-aid math for edit mode (RF-044/M2-RF-444/M2-RF-446, T-15). Given the moving
/// widget's rectangle, the other widgets' rectangles, and the page bounds, it finds the nearest
/// edge/center alignment on each axis within the fixed threshold and returns the resulting offset
/// plus the guides to draw. Alignment is deterministic: for equal distances the earlier moving edge
/// (left/top, then center, then right/bottom) and the earlier target win. It never clamps — the
/// caller composes snapping with the page clamp and the 40 DIP minimum (M2-RF-442).
/// </summary>
public static class AlignmentGuides
{
    /// <summary>Fixed alignment threshold in DIPs (RF-044/M2-RF-444).</summary>
    public const double Threshold = 4;

    /// <summary>
    /// Resolves the alignment offset and guides for one moving widget rectangle against the other
    /// widgets and the page edges/center. Non-finite inputs are normalized to finite values; a
    /// non-finite or non-positive page extent disables the page targets on that axis.
    /// </summary>
    /// <param name="x">The moving widget's horizontal position in DIPs.</param>
    /// <param name="y">The moving widget's vertical position in DIPs.</param>
    /// <param name="width">The moving widget's width in DIPs.</param>
    /// <param name="height">The moving widget's height in DIPs.</param>
    /// <param name="others">The other widgets to align against; the moving widget must be excluded by the caller.</param>
    /// <param name="pageWidth">The page width in DIPs; non-positive/non-finite disables page targets on the X axis.</param>
    /// <param name="pageHeight">The page height in DIPs; non-positive/non-finite disables page targets on the Y axis.</param>
    /// <param name="threshold">The alignment threshold in DIPs; hostile values fall back to <see cref="Threshold"/>.</param>
    /// <returns>The alignment offset and guides; offsets are zero when nothing is within the threshold.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="others"/> is <see langword="null"/>.</exception>
    public static AlignmentSolution Resolve(
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<WidgetGeometry> others,
        double pageWidth,
        double pageHeight,
        double threshold)
    {
        ArgumentNullException.ThrowIfNull(others);

        var safeThreshold = double.IsFinite(threshold) && threshold >= 0 ? threshold : Threshold;
        var safeX = double.IsFinite(x) ? x : 0;
        var safeY = double.IsFinite(y) ? y : 0;
        var safeWidth = double.IsFinite(width) && width > 0 ? width : 0;
        var safeHeight = double.IsFinite(height) && height > 0 ? height : 0;

        var guides = new List<GuideLine>(2);

        var deltaX = FindHorizontalOffset(safeX, safeWidth, safeY, safeHeight, others, pageWidth, pageHeight, safeThreshold, out var verticalGuide);
        if (verticalGuide is { } vg)
        {
            guides.Add(vg);
        }

        var deltaY = FindVerticalOffset(safeY, safeHeight, safeX, safeWidth, others, pageWidth, pageHeight, safeThreshold, out var horizontalGuide);
        if (horizontalGuide is { } hg)
        {
            guides.Add(hg);
        }

        return new AlignmentSolution(deltaX, deltaY, guides);
    }

    private static double FindHorizontalOffset(
        double x,
        double width,
        double y,
        double height,
        IReadOnlyList<WidgetGeometry> others,
        double pageWidth,
        double pageHeight,
        double threshold,
        out GuideLine? guide)
    {
        guide = null;

        var moving = new[] { x, x + (width / 2), x + width };
        var bestMagnitude = double.PositiveInfinity;
        var bestDelta = 0d;
        double? bestTarget = null;

        foreach (var movingLine in moving)
        {
            foreach (var (target, _, _) in EnumerateVerticalTargets(others, pageWidth, pageHeight))
            {
                var delta = target - movingLine;
                var magnitude = Math.Abs(delta);
                if (magnitude > threshold || magnitude >= bestMagnitude)
                {
                    continue;
                }

                bestMagnitude = magnitude;
                bestDelta = delta;
                bestTarget = target;
            }
        }

        if (bestTarget is { } targetLine)
        {
            var end = pageHeight > 0 ? pageHeight : y + height;
            guide = new GuideLine(IsVertical: true, Position: targetLine, Start: 0, End: end);
        }

        return bestDelta;
    }

    private static double FindVerticalOffset(
        double y,
        double height,
        double x,
        double width,
        IReadOnlyList<WidgetGeometry> others,
        double pageWidth,
        double pageHeight,
        double threshold,
        out GuideLine? guide)
    {
        guide = null;

        var moving = new[] { y, y + (height / 2), y + height };
        var bestMagnitude = double.PositiveInfinity;
        var bestDelta = 0d;
        double? bestTarget = null;

        foreach (var movingLine in moving)
        {
            foreach (var (target, _, _) in EnumerateHorizontalTargets(others, pageWidth, pageHeight))
            {
                var delta = target - movingLine;
                var magnitude = Math.Abs(delta);
                if (magnitude > threshold || magnitude >= bestMagnitude)
                {
                    continue;
                }

                bestMagnitude = magnitude;
                bestDelta = delta;
                bestTarget = target;
            }
        }

        if (bestTarget is { } targetLine)
        {
            var end = pageWidth > 0 ? pageWidth : x + width;
            guide = new GuideLine(IsVertical: false, Position: targetLine, Start: 0, End: end);
        }

        return bestDelta;
    }

    /// <summary>
    /// Enumerates the X coordinates to align against (each other widget's left/center/right, then
    /// the page left/center/right when the page width is usable). The perpendicular span is returned
    /// alongside for callers that want a local guide.
    /// </summary>
    private static IEnumerable<(double Position, double Start, double End)> EnumerateVerticalTargets(
        IReadOnlyList<WidgetGeometry> others,
        double pageWidth,
        double pageHeight)
    {
        foreach (var other in others)
        {
            if (!IsUsableRect(other))
            {
                continue;
            }

            yield return (other.X, other.Y, other.Y + other.Height);
            yield return (other.X + (other.Width / 2), other.Y, other.Y + other.Height);
            yield return (other.X + other.Width, other.Y, other.Y + other.Height);
        }

        if (pageWidth > 0 && double.IsFinite(pageWidth))
        {
            yield return (0, 0, pageHeight > 0 ? pageHeight : 0);
            yield return (pageWidth / 2, 0, pageHeight > 0 ? pageHeight : 0);
            yield return (pageWidth, 0, pageHeight > 0 ? pageHeight : 0);
        }
    }

    /// <summary>
    /// Enumerates the Y coordinates to align against (each other widget's top/center/bottom, then
    /// the page top/center/bottom when the page height is usable).
    /// </summary>
    private static IEnumerable<(double Position, double Start, double End)> EnumerateHorizontalTargets(
        IReadOnlyList<WidgetGeometry> others,
        double pageWidth,
        double pageHeight)
    {
        foreach (var other in others)
        {
            if (!IsUsableRect(other))
            {
                continue;
            }

            yield return (other.Y, other.X, other.X + other.Width);
            yield return (other.Y + (other.Height / 2), other.X, other.X + other.Width);
            yield return (other.Y + other.Height, other.X, other.X + other.Width);
        }

        if (pageHeight > 0 && double.IsFinite(pageHeight))
        {
            yield return (0, 0, pageWidth > 0 ? pageWidth : 0);
            yield return (pageHeight / 2, 0, pageWidth > 0 ? pageWidth : 0);
            yield return (pageHeight, 0, pageWidth > 0 ? pageWidth : 0);
        }
    }

    private static bool IsUsableRect(WidgetGeometry rect) =>
        double.IsFinite(rect.X)
        && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width)
        && double.IsFinite(rect.Height);
}
