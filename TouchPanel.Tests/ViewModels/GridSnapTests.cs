using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Pure grid-snap math (RF-044/M2-RF-441/M2-RF-442/M2-RF-446, T-15): grid-size normalization into
/// the 2-64 DIP range, nearest-multiple rounding (deterministic at midpoints), non-finite input
/// normalization, and the "smaller than a grid cell only snaps the origin" extent rule. No UI,
/// hardware, or file system is involved.
/// </summary>
public sealed class GridSnapTests
{
    [Theory]
    [InlineData(8, 8)]
    [InlineData(2, 2)]
    [InlineData(64, 64)]
    [InlineData(1, 2)]
    [InlineData(0, 2)]
    [InlineData(-10, 2)]
    [InlineData(1000, 64)]
    [InlineData(double.NaN, 8)]
    [InlineData(double.PositiveInfinity, 8)]
    public void NormalizeGridSize_ClampsToTheSupportedRange(double input, double expected)
    {
        Assert.Equal(expected, GridSnap.NormalizeGridSize(input));
    }

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(8, 8, 8)]
    [InlineData(13, 8, 16)]
    [InlineData(11, 8, 8)]
    [InlineData(12, 8, 16)]
    [InlineData(4, 8, 8)]
    [InlineData(15, 8, 16)]
    [InlineData(100, 8, 104)]
    public void Snap_RoundsToTheNearestGridMultiple(double value, double grid, double expected)
    {
        Assert.Equal(expected, GridSnap.Snap(value, grid));
    }

    [Fact]
    public void Snap_HostileValueOrGrid_DegradesToFinite()
    {
        Assert.Equal(0, GridSnap.Snap(double.NaN, 8));
        Assert.Equal(0, GridSnap.Snap(double.PositiveInfinity, 8));

        // A hostile grid is normalized: NaN falls back to the default 8, an out-of-range value is
        // clamped to the 2-64 DIP range before snapping.
        Assert.Equal(16, GridSnap.Snap(13, double.NaN));
        Assert.Equal(14, GridSnap.Snap(13, 0)); // grid clamps to 2, so 13 -> 14.
    }

    [Theory]
    [InlineData(100, 8, 104)]
    [InlineData(40, 8, 40)]
    [InlineData(7, 8, 7)] // smaller than a grid cell: the extent passes through.
    [InlineData(4, 8, 4)]
    public void SnapExtent_LeavesASubCellExtentAlone(double extent, double grid, double expected)
    {
        Assert.Equal(expected, GridSnap.SnapExtent(extent, grid));
    }

    [Fact]
    public void SnapExtent_HostileInput_Normalizes()
    {
        Assert.Equal(0, GridSnap.SnapExtent(double.NaN, 8));
        Assert.Equal(0, GridSnap.SnapExtent(double.NegativeInfinity, 8));
    }
}
