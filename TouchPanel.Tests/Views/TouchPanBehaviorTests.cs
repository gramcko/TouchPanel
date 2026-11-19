using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Views.Controls;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Focused STA checks for the touch drag-to-scroll glue (user-reported fix: a vertical drag that
/// starts on a <c>TextBox</c> or its text must scroll the panel). Real touch hardware cannot be
/// injected, so the tests drive the behavior's own internal seam, which runs exactly the same
/// threshold, clamping, and <see cref="ScrollViewer.ScrollToVerticalOffset"/> path as the preview
/// touch handlers. The attached XAML usage is covered separately by the BAML tests.
/// </summary>
public sealed class TouchPanBehaviorTests
{
    [Fact]
    public async Task Behavior_ScrollsOnlyPastTheThresholdAndClampsAtBothEnds()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewer = CreateViewer(contentHeight: 1000, viewportHeight: 200);
            var behavior = TouchPan.GetBehaviorForTest(viewer);
            Assert.NotNull(behavior);

            var scrollable = viewer.ScrollableHeight;
            var beforeThreshold = 0d;
            var afterStart = 0d;
            var atBottom = 0d;
            var atTop = 0d;

            Assert.True(behavior.BeginForTest(150, 120));
            Assert.False(behavior.MoveForTest(150, 116).IsPanning);
            viewer.UpdateLayout();
            beforeThreshold = viewer.VerticalOffset;

            Assert.True(behavior.MoveForTest(150, 100).IsPanning);
            viewer.UpdateLayout();
            afterStart = viewer.VerticalOffset;

            behavior.MoveForTest(150, -1000);
            viewer.UpdateLayout();
            atBottom = viewer.VerticalOffset;

            behavior.MoveForTest(150, 2000);
            viewer.UpdateLayout();
            atTop = viewer.VerticalOffset;

            var endedPan = behavior.EndForTest();
            return (scrollable, beforeThreshold, afterStart, atBottom, atTop, endedPan);
        });

        Assert.Equal(800, result.scrollable, precision: 3);
        Assert.Equal(0, result.beforeThreshold);
        Assert.Equal(20, result.afterStart, precision: 3);
        Assert.Equal(800, result.atBottom, precision: 3);
        Assert.Equal(0, result.atTop, precision: 3);
        Assert.True(result.endedPan);
    }

    [Fact]
    public async Task Behavior_CancelLeavesTheOffsetAndAllowsANewSession()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewer = CreateViewer(contentHeight: 1000, viewportHeight: 200);
            var behavior = TouchPan.GetBehaviorForTest(viewer);
            Assert.NotNull(behavior);

            Assert.True(behavior.BeginForTest(150, 120));
            behavior.MoveForTest(150, 100);
            viewer.UpdateLayout();
            var panned = viewer.VerticalOffset;

            behavior.CancelForTest();
            viewer.UpdateLayout();
            var offsetAfterCancel = viewer.VerticalOffset;

            // The cancelled contact can no longer pan.
            behavior.MoveForTest(150, -1000);
            viewer.UpdateLayout();
            var offsetAfterCancelledMove = viewer.VerticalOffset;

            Assert.True(behavior.BeginForTest(150, 120));
            Assert.True(behavior.MoveForTest(150, 100).IsPanning);
            viewer.UpdateLayout();
            var offsetAfterNewSession = viewer.VerticalOffset;

            return (panned, offsetAfterCancel, offsetAfterCancelledMove, offsetAfterNewSession);
        });

        Assert.Equal(20, result.panned, precision: 3);
        Assert.Equal(20, result.offsetAfterCancel, precision: 3);
        Assert.Equal(20, result.offsetAfterCancelledMove, precision: 3);
        Assert.Equal(40, result.offsetAfterNewSession, precision: 3);
    }

    [Fact]
    public async Task Behavior_DisablesItselfWhenTheAttachedFlagTurnsOff()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewer = CreateViewer(contentHeight: 1000, viewportHeight: 200);
            var attached = TouchPan.GetBehaviorForTest(viewer) is not null;
            TouchPan.SetIsEnabled(viewer, false);
            return (attached, detached: TouchPan.GetBehaviorForTest(viewer) is null);
        });

        Assert.True(result.attached);
        Assert.True(result.detached);
    }

    [Fact]
    public async Task Behavior_IgnoresAViewerWithNothingToScroll()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewer = CreateViewer(contentHeight: 100, viewportHeight: 200);
            var behavior = TouchPan.GetBehaviorForTest(viewer);
            Assert.NotNull(behavior);
            var began = behavior.BeginForTest(150, 120);
            behavior.MoveForTest(150, -1000);
            return (scrollable: viewer.ScrollableHeight, began, offset: viewer.VerticalOffset);
        });

        Assert.Equal(0, result.scrollable);
        Assert.False(result.began);
        Assert.Equal(0, result.offset);
    }

    [Fact]
    public async Task Behavior_IgnoresTheVerticalScrollBarAndExcludedSubtrees()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewer = CreateViewer(contentHeight: 1000, viewportHeight: 200);
            viewer.ApplyTemplate();
            var behavior = TouchPan.GetBehaviorForTest(viewer);
            Assert.NotNull(behavior);

            var verticalBar = Assert.IsAssignableFrom<ScrollBar>(
                viewer.Template.FindName("PART_VerticalScrollBar", viewer));

            var fromScrollBar = behavior.BeginForTest(150, 120, verticalBar);
            behavior.EndForTest();

            var excluded = new Border();
            TouchPan.SetIsPanExcluded(excluded, true);
            var fromExcluded = behavior.BeginForTest(150, 120, excluded);
            behavior.EndForTest();

            var fromContent = behavior.BeginForTest(150, 120, viewer);

            return (fromScrollBar, fromExcluded, fromContent, offset: viewer.VerticalOffset);
        });

        Assert.False(result.fromScrollBar);
        Assert.False(result.fromExcluded);
        Assert.True(result.fromContent);
        Assert.Equal(0, result.offset);
    }

    private static ScrollViewer CreateViewer(double contentHeight, double viewportHeight)
    {
        var viewer = new ScrollViewer
        {
            Width = 300,
            Height = viewportHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Width = 280, Height = contentHeight },
        };

        TouchPan.SetIsEnabled(viewer, true);
        viewer.Measure(new Size(300, viewportHeight));
        viewer.Arrange(new Rect(0, 0, 300, viewportHeight));
        viewer.UpdateLayout();
        return viewer;
    }
}
