using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Pure drag-to-reorder decisions (no UI automation): the press-to-drag threshold and direction, the
/// insertion slot between row midpoints, and the conversion from slot to final list index. These are
/// the rules both pages surfaces use; the pointer wiring itself stays in the shared view.
/// </summary>
public sealed class PageReorderDragPolicyTests
{
    [Fact]
    public void ShouldStartDrag_RequiresDeliberateVerticalTravel()
    {
        Assert.False(PageReorderDragPolicy.ShouldStartDrag(0, 0));
        Assert.False(PageReorderDragPolicy.ShouldStartDrag(2, 11));
        Assert.True(PageReorderDragPolicy.ShouldStartDrag(0, 12));
        Assert.True(PageReorderDragPolicy.ShouldStartDrag(4, 13));
        Assert.True(PageReorderDragPolicy.ShouldStartDrag(0, -12));
    }

    [Fact]
    public void ShouldStartDrag_IgnoresHorizontalDominantGestures()
    {
        Assert.False(PageReorderDragPolicy.ShouldStartDrag(40, 12));
        Assert.False(PageReorderDragPolicy.ShouldStartDrag(12, 11));
        Assert.True(PageReorderDragPolicy.ShouldStartDrag(11, 12));
    }

    [Fact]
    public void ShouldStartDrag_UsesTheDefaultForNonPositiveThresholds_AndRejectsNonFiniteDeltas()
    {
        Assert.True(PageReorderDragPolicy.ShouldStartDrag(0, 12, threshold: 0));
        Assert.True(PageReorderDragPolicy.ShouldStartDrag(0, 30, threshold: -5));
        Assert.False(PageReorderDragPolicy.ShouldStartDrag(0, 20, threshold: 25));

        Assert.False(PageReorderDragPolicy.ShouldStartDrag(double.NaN, 20));
        Assert.False(PageReorderDragPolicy.ShouldStartDrag(0, double.PositiveInfinity));
    }

    [Fact]
    public void ResolveInsertionIndex_UsesRowMidpoints()
    {
        var rows = new[]
        {
            new PageRowExtent(0, 50),
            new PageRowExtent(56, 50),
            new PageRowExtent(112, 50),
        };

        Assert.Equal(0, PageReorderDragPolicy.ResolveInsertionIndex(rows, -10));
        Assert.Equal(0, PageReorderDragPolicy.ResolveInsertionIndex(rows, 24));
        Assert.Equal(1, PageReorderDragPolicy.ResolveInsertionIndex(rows, 26));
        Assert.Equal(1, PageReorderDragPolicy.ResolveInsertionIndex(rows, 80));
        Assert.Equal(2, PageReorderDragPolicy.ResolveInsertionIndex(rows, 90));
        Assert.Equal(3, PageReorderDragPolicy.ResolveInsertionIndex(rows, 200));
    }

    [Fact]
    public void ResolveInsertionIndex_WithNoRows_IsSlotZero()
    {
        Assert.Equal(0, PageReorderDragPolicy.ResolveInsertionIndex([], 100));
    }

    [Fact]
    public void ResolveTargetIndex_AccountsForTheDraggedRowStillBeingInTheList()
    {
        // Dropping into the slot below the row itself is a no-op; the next slot means one position down.
        Assert.Equal(1, PageReorderDragPolicy.ResolveTargetIndex(0, 2, 3));
        Assert.Equal(0, PageReorderDragPolicy.ResolveTargetIndex(0, 1, 3));
        Assert.Equal(0, PageReorderDragPolicy.ResolveTargetIndex(2, 0, 3));
        Assert.Equal(2, PageReorderDragPolicy.ResolveTargetIndex(2, 2, 3));
        Assert.Equal(1, PageReorderDragPolicy.ResolveTargetIndex(2, 1, 3));
        Assert.Equal(2, PageReorderDragPolicy.ResolveTargetIndex(0, 3, 3));
        Assert.Equal(0, PageReorderDragPolicy.ResolveTargetIndex(1, 0, 3));
    }

    [Fact]
    public void ResolveTargetIndex_ClampsAndRejectsInvalidSources()
    {
        Assert.Equal(0, PageReorderDragPolicy.ResolveTargetIndex(0, -20, 2));
        Assert.Equal(1, PageReorderDragPolicy.ResolveTargetIndex(1, 99, 2));
        Assert.Equal(-1, PageReorderDragPolicy.ResolveTargetIndex(0, 0, 0));
        Assert.Equal(-1, PageReorderDragPolicy.ResolveTargetIndex(-1, 0, 3));
        Assert.Equal(-1, PageReorderDragPolicy.ResolveTargetIndex(3, 0, 3));
    }
}
