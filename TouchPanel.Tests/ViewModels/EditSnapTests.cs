using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Edit-mode grid snap and alignment composition (RF-044/M2-RF-441..446, T-15): drag/resize snapping,
/// the free-position override and the snap toggle, alignment overriding grid when a target is within
/// the 4 DIP threshold, the snap-then-clamp-then-minimum order, grid-size normalization, transient
/// guide exposure during a drag, and dirty tracking. No hardware, files, or UI are involved.
/// </summary>
public sealed class EditSnapTests
{
    private const string PageId = "main";

    [Fact]
    public void MoveWidgetBy_WithSnapOn_SnapsTheOriginToTheGrid()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100), Label("w2", 600, 10, 100, 100));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.MoveWidgetBy(context.Widget, 13, 13));

        // Raw 23 -> nearest 8 DIP multiple 24; the alignment targets (including w2 at 600 and the
        // page center) are far away, so grid snapping decides.
        Assert.Equal(24, context.Widget.X);
        Assert.Equal(24, context.Widget.Y);
        Assert.True(context.Service.IsDirty);
        Assert.Empty(context.Service.ActiveGuides);
    }

    [Fact]
    public void MoveWidgetBy_WithSnapOff_IsFreePositioning()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.ToggleSnap());

        Assert.True(context.Service.MoveWidgetBy(context.Widget, 13, 13));

        Assert.Equal(23, context.Widget.X);
        Assert.Equal(23, context.Widget.Y);
        Assert.Empty(context.Service.ActiveGuides);
    }

    [Fact]
    public void MoveWidgetBy_FreePositionOverride_BypassesSnapButKeepsClamping()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.MoveWidgetBy(context.Widget, 13, 13, freeOverride: true));

        Assert.Equal(23, context.Widget.X);
        Assert.Equal(23, context.Widget.Y);
        Assert.Empty(context.Service.ActiveGuides);
    }

    [Fact]
    public void MoveWidgetBy_AlignmentOverridesGrid_WhenWithinThreshold()
    {
        // Raw left edge 101 snaps to 104, but w2's left edge at 100 is 4 DIP away, so alignment
        // wins and pins the widget to 100 (M2-RF-444 precedence).
        var context = CreateContext(
            Label("w1", 90, 0, 100, 100),
            Label("w2", 100, 0, 50, 50));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.MoveWidgetBy(context.Widget, 11, 0));

        Assert.Equal(100, context.Widget.X);
        Assert.Contains(
            context.Service.ActiveGuides,
            guide => guide.IsVertical && guide.Position == 100);
    }

    [Fact]
    public void MoveWidgetBy_SnapsThenClamps_AtThePageEdge()
    {
        var context = CreateContext(Label("w1", 50, 0, 100, 100));
        context.Service.Enter(200, 500);

        // Raw 550 snaps to 552, then the page clamp pins it to page width - widget width.
        Assert.True(context.Service.MoveWidgetBy(context.Widget, 500, 0));

        Assert.Equal(100, context.Widget.X);
        Assert.True(double.IsFinite(context.Widget.X));
    }

    [Fact]
    public void MoveWidgetBy_HostileDelta_ProducesFiniteSnappedGeometry()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.MoveWidgetBy(context.Widget, double.NaN, double.PositiveInfinity));

        // Non-finite drag deltas normalize to a finite snapped position (0) instead of propagating.
        Assert.True(double.IsFinite(context.Widget.X));
        Assert.True(double.IsFinite(context.Widget.Y));
        Assert.Equal(0, context.Widget.X);
        Assert.Equal(0, context.Widget.Y);
    }

    [Fact]
    public void ResizeWidgetBy_WithSnapOn_SnapsTheExtentsThenClamps()
    {
        var context = CreateContext(Label("w1", 0, 0, 100, 100));
        context.Service.Enter(1000, 500);

        // 100 + 3 = 103 -> 104; then the minimum/clamp pipeline applies.
        Assert.True(context.Service.ResizeWidgetBy(context.Widget, 3, 3));

        Assert.Equal(104, context.Widget.Width);
        Assert.Equal(104, context.Widget.Height);
        Assert.Empty(context.Service.ActiveGuides);
    }

    [Fact]
    public void ResizeWidgetBy_FreeOverride_BypassesSnap()
    {
        var context = CreateContext(Label("w1", 0, 0, 100, 100));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.ResizeWidgetBy(context.Widget, 3, 3, freeOverride: true));

        Assert.Equal(103, context.Widget.Width);
        Assert.Equal(103, context.Widget.Height);
    }

    [Fact]
    public void ActiveGuides_ArePopulatedWhileDraggingAndClearedWhenTheDragEnds()
    {
        var context = CreateContext(
            Label("w1", 90, 0, 100, 100),
            Label("w2", 100, 0, 50, 50));
        context.Service.Enter(1000, 500);

        context.Service.BeginDrag(context.Widget);
        Assert.Empty(context.Service.ActiveGuides);

        context.Service.MoveWidgetBy(context.Widget, 11, 0);
        Assert.NotEmpty(context.Service.ActiveGuides);

        context.Service.EndDrag();
        Assert.Empty(context.Service.ActiveGuides);
    }

    [Fact]
    public void SetGridSize_NormalizesAndDrivesNudging()
    {
        var context = CreateContext(Label("w1", 100, 100, 200, 100));
        context.Service.Enter(1000, 500);

        context.Service.SetGridSize(500);
        Assert.Equal(64, context.Service.GridSize);

        context.Service.SetGridSize(double.NaN);
        Assert.Equal(8, context.Service.GridSize);

        context.Service.SetGridSize(4);
        context.Service.SelectWidget(context.Widget);
        Assert.True(context.Service.NudgeSelectedWidget(1, 0));
        Assert.Equal(104, context.Widget.X);
    }

    [Fact]
    public void SetSnapEnabled_OutsideEditMode_IsInert()
    {
        var context = CreateContext(Label("w1", 0, 0, 200, 100));

        Assert.False(context.Service.SetSnapEnabled(false));
        Assert.True(context.Service.IsSnapEnabled);
    }

    [Fact]
    public void SetSnapEnabled_Off_ClearsGuidesAndRestoresFreePositioning()
    {
        var context = CreateContext(
            Label("w1", 90, 0, 100, 100),
            Label("w2", 100, 0, 50, 50));
        context.Service.Enter(1000, 500);

        context.Service.BeginDrag(context.Widget);
        context.Service.MoveWidgetBy(context.Widget, 11, 0);
        Assert.NotEmpty(context.Service.ActiveGuides);

        Assert.True(context.Service.SetSnapEnabled(false));
        Assert.Empty(context.Service.ActiveGuides);

        // Subsequent moves are free positioning.
        context.Service.MoveWidgetBy(context.Widget, 1, 1);
        Assert.Equal(101, context.Widget.X);
        Assert.Equal(1, context.Widget.Y);
    }

    private static TestContext CreateContext(params WidgetConfig[] widgets)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = PageId,
            Pages = [new PageConfig { Id = PageId, Widgets = [.. widgets] }],
        };
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore());
        return new TestContext(page, service);
    }

    private static LabelWidgetConfig Label(string id, double x, double y, double width, double height) => new()
    {
        Id = id,
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Label = id,
    };

    private sealed record TestContext(PageViewModel Page, EditModeService Service)
    {
        public WidgetViewModelBase Widget => Page.Widgets[0];
    }
}
