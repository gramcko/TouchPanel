using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Pure alignment-guide math (RF-044/M2-RF-444/M2-RF-446, T-15): edge/center alignment within the
/// fixed 4 DIP threshold against other widgets and the page, the resulting snap offset, guide
/// emission for the active axes, deterministic tie-breaking, hostile input normalization, and the
/// fact that the math never clamps (the clamp/minimum composition lives in the edit service).
/// </summary>
public sealed class AlignmentGuidesTests
{
    private const double Threshold = AlignmentGuides.Threshold;

    [Fact]
    public void Resolve_NoTargetWithinThreshold_ReturnsNoOffsetOrGuides()
    {
        var solution = AlignmentGuides.Resolve(
            120,
            120,
            50,
            50,
            [new WidgetGeometry(100, 100, 50, 50)],
            1000,
            1000,
            Threshold);

        Assert.Equal(0, solution.DeltaX);
        Assert.Equal(0, solution.DeltaY);
        Assert.Empty(solution.Guides);
    }

    [Fact]
    public void Resolve_EdgeWithinThreshold_ReturnsOffsetAndAVerticalGuide()
    {
        // The moving left edge at 103 is 3 DIP from the other widget's left edge at 100.
        var solution = AlignmentGuides.Resolve(
            103,
            120,
            50,
            50,
            [new WidgetGeometry(100, 100, 50, 50)],
            1000,
            1000,
            Threshold);

        Assert.Equal(-3, solution.DeltaX);
        Assert.Equal(0, solution.DeltaY);

        var guide = Assert.Single(solution.Guides);
        Assert.True(guide.IsVertical);
        Assert.Equal(100, guide.Position);
    }

    [Fact]
    public void Resolve_CenterAlignment_MatchesTheOtherWidgetCenter()
    {
        // Moving center = 128, other center = 125 -> -3 offset; other bottom at 150 pulls the
        // vertical axis but the moving center 145 is 5 away (outside the 4 DIP threshold).
        var solution = AlignmentGuides.Resolve(
            103,
            120,
            50,
            50,
            [new WidgetGeometry(100, 100, 50, 50)],
            1000,
            1000,
            Threshold);

        // The left edge is the first moving line and wins the tie at -3, aligning to x = 100.
        Assert.Equal(-3, solution.DeltaX);
        Assert.Single(solution.Guides);
    }

    [Fact]
    public void Resolve_PageCenter_AlignsOnBothAxes()
    {
        // The moving widget center is exactly the page center on both axes.
        var solution = AlignmentGuides.Resolve(475, 475, 50, 50, [], 1000, 1000, Threshold);

        Assert.Equal(0, solution.DeltaX);
        Assert.Equal(0, solution.DeltaY);
        Assert.Equal(2, solution.Guides.Count);
        Assert.Contains(solution.Guides, guide => guide.IsVertical && guide.Position == 500);
        Assert.Contains(solution.Guides, guide => !guide.IsVertical && guide.Position == 500);
    }

    [Fact]
    public void Resolve_ThresholdIsInclusive()
    {
        // Exactly at the threshold aligns; one DIP beyond does not.
        var atThreshold = AlignmentGuides.Resolve(
            104, 120, 50, 50, [new WidgetGeometry(100, 100, 50, 50)], 1000, 1000, Threshold);
        Assert.Equal(-4, atThreshold.DeltaX);

        var beyond = AlignmentGuides.Resolve(
            105, 120, 50, 50, [new WidgetGeometry(100, 100, 50, 50)], 1000, 1000, Threshold);
        Assert.Equal(0, beyond.DeltaX);
    }

    [Fact]
    public void Resolve_DoesNotClamp_NegativeOffsetIsReturned()
    {
        var solution = AlignmentGuides.Resolve(
            3, 120, 50, 50, [], 1000, 1000, Threshold);

        // The moving left edge snaps to the page left edge (0), producing a negative offset that
        // the caller is responsible for clamping.
        Assert.Equal(-3, solution.DeltaX);
    }

    [Fact]
    public void Resolve_NonFiniteInputs_AreNormalized()
    {
        var solution = AlignmentGuides.Resolve(
            double.NaN,
            double.PositiveInfinity,
            double.NaN,
            -10,
            [new WidgetGeometry(double.NaN, 0, 50, 50)],
            1000,
            1000,
            Threshold);

        Assert.True(double.IsFinite(solution.DeltaX));
        Assert.True(double.IsFinite(solution.DeltaY));
    }

    [Fact]
    public void Resolve_UnknownPageBounds_DisablesPageTargets()
    {
        var solution = AlignmentGuides.Resolve(
            3, 3, 50, 50, [], 0, 0, Threshold);

        Assert.Equal(0, solution.DeltaX);
        Assert.Equal(0, solution.DeltaY);
        Assert.Empty(solution.Guides);
    }
}
