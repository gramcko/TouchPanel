using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Edit-mode behavior (T-12, RF-026/RF-027/RF-039/RF-077): move/resize clamping against the page
/// bounds and the 40 DIP minimum, hostile geometry values, selection and 1 DIP arrow nudging,
/// dirty tracking, save-on-exit through the store only when dirty, failed/canceled saves keeping
/// the in-memory layout and the dirty flag, geometry round-tripping into the configuration models,
/// and the synchronous non-activating flag flip on exit. No file, hardware, or user profile is
/// touched except in the dedicated temp-directory round trip.
/// </summary>
public sealed class EditModeServiceTests
{
    private const string PageId = "main";

    [Fact]
    public void Enter_TracksStateAndClampBounds()
    {
        var context = CreateContext(Label("w1", 0, 0, 200, 100));

        Assert.False(context.Service.IsEditMode);
        Assert.Equal(0, context.Service.PageWidth);

        context.Service.Enter(1024, 600);

        Assert.True(context.Service.IsEditMode);
        Assert.Equal(1024, context.Service.PageWidth);
        Assert.Equal(600, context.Service.PageHeight);
    }

    [Fact]
    public async Task EditMode_FlagPropagatesToPageWidgetsAndWidgetsAddedWhileEditing()
    {
        // M2-RF-439: EditModeService owns the per-widget edit-mode state used to suppress activation.
        var context = CreateContext(Label("w1", 0, 0, 200, 100));
        Assert.False(context.Widget.IsEditMode);

        context.Service.Enter(1000, 500);
        Assert.True(context.Widget.IsEditMode);

        Assert.True(context.Service.AddWidget(WidgetTypes.Button));
        var added = context.Page.Widgets[1];
        Assert.True(added.IsEditMode);

        await context.Service.ExitAsync();
        Assert.False(context.Widget.IsEditMode);
        Assert.False(added.IsEditMode);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-100)]
    public void Enter_HostilePageBounds_DegradeToNoPracticalBound(double hostile)
    {
        var context = CreateContext(Label("w1", 0, 0, 200, 100));

        context.Service.Enter(hostile, hostile);
        context.Service.MoveWidget(context.Widget, 500, 500);

        // The page has no positive extent, so the extents are preserved and the finite requested
        // position is kept (nothing collapses to the origin or becomes non-finite).
        Assert.Equal(0, context.Service.PageWidth);
        Assert.Equal(500, context.Widget.X);
        Assert.Equal(500, context.Widget.Y);
        Assert.Equal(200, context.Widget.Width);
    }

    [Fact]
    public void MoveWidget_AtEdgesAndCorners_ClampsIntoThePage()
    {
        var context = CreateContext(Label("w1", 300, 200, 200, 100));
        context.Service.Enter(1000, 500);

        // Top-left overflow.
        Assert.True(context.Service.MoveWidget(context.Widget, -50, -50));
        Assert.Equal(0, context.Widget.X);
        Assert.Equal(0, context.Widget.Y);

        // Bottom-right overflow: pinned to page - extent.
        Assert.True(context.Service.MoveWidget(context.Widget, 5000, 5000));
        Assert.Equal(800, context.Widget.X);
        Assert.Equal(400, context.Widget.Y);

        // Both corners already cover the edge cases; a valid interior position passes through.
        Assert.True(context.Service.MoveWidget(context.Widget, 12.5, 34.5));
        Assert.Equal(12.5, context.Widget.X);
        Assert.Equal(34.5, context.Widget.Y);
    }

    [Fact]
    public void MoveWidget_HostileValues_ProduceFiniteGeometry()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.MoveWidget(context.Widget, double.NaN, double.PositiveInfinity));

        Assert.Equal(0, context.Widget.X);
        Assert.Equal(400, context.Widget.Y); // +Infinity clamps to the page bottom minus the extent.
        Assert.True(double.IsFinite(context.Widget.X));
        Assert.True(double.IsFinite(context.Widget.Y));
    }

    [Fact]
    public void ResizeWidget_BelowTheMinimum_EnforcesFortyDips()
    {
        var context = CreateContext(Label("w1", 100, 100, 200, 100));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.ResizeWidget(context.Widget, 5, -20));

        Assert.Equal(40, context.Widget.Width);
        Assert.Equal(40, context.Widget.Height);
        Assert.Equal(100, context.Widget.X);
        Assert.Equal(100, context.Widget.Y);
    }

    [Fact]
    public void ResizeWidget_HostileValues_FallBackToTheMinimum()
    {
        var context = CreateContext(Label("w1", 100, 100, 200, 100));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.ResizeWidget(context.Widget, double.NaN, double.NegativeInfinity));

        Assert.Equal(40, context.Widget.Width);
        Assert.Equal(40, context.Widget.Height);
    }

    [Fact]
    public void ResizeWidget_AtTheBottomRightCorner_LimitsExtentsWithoutMovingTheWidget()
    {
        var context = CreateContext(Label("w1", 900, 450, 50, 20));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.ResizeWidget(context.Widget, 500, 500));

        // The position is preserved and the extent grows only to the remaining page space.
        Assert.Equal(900, context.Widget.X);
        Assert.Equal(450, context.Widget.Y);
        Assert.Equal(100, context.Widget.Width);
        Assert.Equal(50, context.Widget.Height);
    }

    [Fact]
    public void MoveWidgetBy_OutOfBoundsDrag_PinsAtTheEdge()
    {
        var context = CreateContext(Label("w1", 100, 100, 200, 100));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.MoveWidgetBy(context.Widget, 5000, -5000));

        Assert.Equal(800, context.Widget.X);
        Assert.Equal(0, context.Widget.Y);
    }

    [Fact]
    public void ResizeWidgetBy_BeyondThePage_LimitsTheExtentWithoutMovingTheWidget()
    {
        var context = CreateContext(Label("w1", 800, 400, 100, 50));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.ResizeWidgetBy(context.Widget, 500, 500));

        Assert.Equal(800, context.Widget.X);
        Assert.Equal(400, context.Widget.Y);
        Assert.Equal(200, context.Widget.Width);
        Assert.Equal(100, context.Widget.Height);
    }

    [Fact]
    public void ResizeWidget_OnAPageSmallerThanTheMinimum_KeepsTheTouchMinimum()
    {
        var context = CreateContext(Label("w1", 0, 0, 100, 100));
        context.Service.Enter(30, 30);

        Assert.True(context.Service.ResizeWidget(context.Widget, 10, 10));

        Assert.Equal(EditModeService.MinimumWidgetSize, context.Widget.Width);
        Assert.Equal(EditModeService.MinimumWidgetSize, context.Widget.Height);
    }

    [Fact]
    public void MoveAndResize_OutsideEditMode_AreIgnoredAndDoNotDirty()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));

        Assert.False(context.Service.MoveWidget(context.Widget, 400, 400));
        Assert.False(context.Service.ResizeWidget(context.Widget, 400, 400));
        Assert.Equal(10, context.Widget.X);
        Assert.Equal(200, context.Widget.Width);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void MoveWidget_ForAWidgetOutsideThePage_IsIgnored()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        var foreign = CreateWidget("foreign");
        context.Service.Enter(1000, 500);

        Assert.False(context.Service.MoveWidget(foreign, 100, 100));
        Assert.Equal(0, foreign.X);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void MoveWidget_ForAnUnknownWidgetType_IsIgnored()
    {
        // Unknown types keep their raw JSON, so edit mode cannot persist their geometry (RF-079).
        using var raw = JsonDocument.Parse("{}");
        var config = ConfigWith(new UnknownWidgetConfig("heatmap", raw.RootElement.Clone()));
        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
        var store = new RecordingConfigStore();
        var service = new EditModeService(config, page, store);
        service.Enter(1000, 500);

        var widget = Assert.Single(page.Widgets);
        Assert.False(widget.CanEditGeometry);
        Assert.False(service.MoveWidget(widget, 100, 100));
        Assert.False(service.ResizeWidget(widget, 300, 300));
        Assert.Equal(0, widget.X);
        Assert.False(service.IsDirty);
    }

    [Fact]
    public void SelectWidget_TracksASingleSelection()
    {
        var context = CreateContext(
            Label("w1", 0, 0, 200, 100),
            Label("w2", 0, 200, 200, 100));

        context.Service.SelectWidget(context.Widgets[0]);
        Assert.Same(context.Widgets[0], context.Service.SelectedWidget);
        Assert.True(context.Widgets[0].IsSelected);
        Assert.False(context.Widgets[1].IsSelected);

        context.Service.SelectWidget(context.Widgets[1]);
        Assert.Same(context.Widgets[1], context.Service.SelectedWidget);
        Assert.False(context.Widgets[0].IsSelected);
        Assert.True(context.Widgets[1].IsSelected);

        context.Service.SelectWidget(null);
        Assert.Null(context.Service.SelectedWidget);
        Assert.False(context.Widgets[1].IsSelected);
    }

    [Fact]
    public void NudgeSelectedWidget_WithSnapOff_MovesExactlyOneDipPerStep()
    {
        var context = CreateContext(Label("w1", 100, 100, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.ToggleSnap();
        context.Service.SelectWidget(context.Widget);

        Assert.True(context.Service.NudgeSelectedWidget(1, 0));
        Assert.True(context.Service.NudgeSelectedWidget(1, 0));
        Assert.True(context.Service.NudgeSelectedWidget(0, -1));

        Assert.Equal(102, context.Widget.X);
        Assert.Equal(99, context.Widget.Y);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void NudgeSelectedWidget_WithSnapOn_MovesByTheGridSize()
    {
        // M2-RF-443: with snap on a nudge moves one grid size (default 8 DIP).
        var context = CreateContext(Label("w1", 100, 100, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widget);

        Assert.True(context.Service.NudgeSelectedWidget(1, 0));
        Assert.True(context.Service.NudgeSelectedWidget(0, 1));

        Assert.Equal(108, context.Widget.X);
        Assert.Equal(108, context.Widget.Y);
    }

    [Fact]
    public void NudgeSelectedWidget_AtTheEdge_ClampsAndReportsNoFurtherChange()
    {
        var context = CreateContext(Label("w1", 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widget);

        Assert.False(context.Service.NudgeSelectedWidget(-1, 0));
        Assert.False(context.Service.NudgeSelectedWidget(0, -1));
        Assert.Equal(0, context.Widget.X);
        Assert.Equal(0, context.Widget.Y);
    }

    [Fact]
    public void NudgeSelectedWidget_WithoutASelection_IsNoOp()
    {
        var context = CreateContext(Label("w1", 100, 100, 200, 100));
        context.Service.Enter(1000, 500);

        Assert.False(context.Service.NudgeSelectedWidget(1, 0));
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public async Task Exit_WithoutChanges_DoesNotTouchTheStore()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);

        var result = await context.Service.ExitAsync();

        Assert.False(result.Attempted);
        Assert.True(result.Succeeded);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.False(context.Service.IsEditMode);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public async Task Exit_WithChanges_SavesOnceThroughTheStore()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.MoveWidget(context.Widget, 120, 130));
        Assert.True(context.Service.IsDirty);

        var result = await context.Service.ExitAsync();

        Assert.True(result.Attempted);
        Assert.True(result.Succeeded);
        Assert.Equal(1, context.Store.SaveCallCount);
        Assert.False(context.Service.IsDirty);
        Assert.False(context.Service.IsEditMode);

        // A second exit has nothing dirty left to persist.
        var second = await context.Service.ExitAsync();
        Assert.False(second.Attempted);
        Assert.Equal(1, context.Store.SaveCallCount);
    }

    [Fact]
    public async Task Exit_WritesTheEditedGeometryBackIntoTheConfigurationModels()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.MoveWidget(context.Widget, 60, 70));
        Assert.True(context.Service.ResizeWidget(context.Widget, 320, 180));

        await context.Service.ExitAsync();

        var savedWidget = Assert.Single(context.Store.LastSavedConfig!.Pages[0].Widgets);
        Assert.Equal(60, savedWidget.X);
        Assert.Equal(70, savedWidget.Y);
        Assert.Equal(320, savedWidget.Width);
        Assert.Equal(180, savedWidget.Height);
    }

    [Fact]
    public async Task Exit_WhenTheSaveFails_KeepsDirtyAndTheInMemoryLayout_AndNotifies()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Store.SaveSucceeds = false;
        context.Store.FailureMessage = "Simulated disk failure.";
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.MoveWidget(context.Widget, 400, 300));

        var result = await context.Service.ExitAsync();

        Assert.True(result.Attempted);
        Assert.False(result.Succeeded);
        Assert.True(context.Service.IsDirty);
        Assert.Equal(400, context.Widget.X);
        Assert.Equal(300, context.Widget.Y);
        Assert.Equal(400, context.Config.Pages[0].Widgets[0].X);

        var notification = Assert.Single(context.Notifier.Notifications);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.Contains("Simulated disk failure.", notification.Message, StringComparison.Ordinal);

        // The next exit retries the same dirty layout.
        context.Store.SaveSucceeds = true;
        var retry = await context.Service.ExitAsync();
        Assert.True(retry.Succeeded);
        Assert.False(context.Service.IsDirty);
        Assert.Equal(2, context.Store.SaveCallCount);
    }

    [Fact]
    public async Task Exit_WhenTheSaveIsCanceled_KeepsDirtyAndStaysRetryable()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Store.SaveHandler = _ => throw new OperationCanceledException();
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.MoveWidget(context.Widget, 40, 40));

        var result = await context.Service.ExitAsync();

        Assert.True(result.Attempted);
        Assert.False(result.Succeeded);
        Assert.True(result.Canceled);
        Assert.True(context.Service.IsDirty);
        Assert.Equal(40, context.Widget.X);
    }

    [Fact]
    public async Task Exit_FlipsTheEditModeFlagSynchronouslyBeforeTheSaveCompletes()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        var gate = new TaskCompletionSource<ConfigSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Store.SaveAsyncHandler = _ => new ValueTask<ConfigSaveResult>(gate.Task);
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.MoveWidget(context.Widget, 40, 40));

        var exitTask = context.Service.ExitAsync();

        // RF-018: non-activating behavior is restored before any save I/O finishes.
        Assert.False(context.Service.IsEditMode);
        Assert.False(exitTask.IsCompleted);

        gate.SetResult(ConfigSaveResult.Success());
        var result = await exitTask;

        Assert.True(result.Succeeded);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public async Task Exit_ThroughTheRealStore_PersistsTheLayoutAcrossReload()
    {
        using var temp = new TempDirectory();
        var config = ConfigWith(Label("w1", 10, 10, 200, 100));
        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
        var store = new JsonConfigStore(temp.Config);
        var service = new EditModeService(config, page, store);

        service.Enter(1024, 600);
        Assert.True(service.MoveWidget(page.Widgets[0], 120, 80));
        Assert.True(service.ResizeWidget(page.Widgets[0], 300, 150));

        var result = await service.ExitAsync();
        Assert.True(result.Succeeded);

        var reloaded = await new JsonConfigStore(temp.Config).LoadAsync();
        Assert.Equal(ConfigState.Current, reloaded.State);
        var reloadedWidget = Assert.Single(reloaded.Config!.Pages[0].Widgets);
        Assert.Equal(120, reloadedWidget.X);
        Assert.Equal(80, reloadedWidget.Y);
        Assert.Equal(300, reloadedWidget.Width);
        Assert.Equal(150, reloadedWidget.Height);
    }

    [Fact]
    public async Task SaveAsync_WhenNothingIsDirty_IsANoOp()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));

        var result = await context.Service.SaveAsync();

        Assert.False(result.Attempted);
        Assert.True(result.Succeeded);
        Assert.Equal(0, context.Store.SaveCallCount);
    }

    [Fact]
    public async Task ToggleSnap_WithinEditMode_FlipsAndResetsOnANewSession()
    {
        var context = CreateContext(Label("w1", 0, 0, 200, 100));

        // Outside edit mode the toggle is inert (RF-060/M2-RF-602).
        Assert.True(context.Service.IsSnapEnabled);
        Assert.False(context.Service.ToggleSnap());
        Assert.True(context.Service.IsSnapEnabled);

        context.Service.Enter(1000, 500);
        Assert.True(context.Service.ToggleSnap());
        Assert.False(context.Service.IsSnapEnabled);

        await context.Service.ExitAsync();

        // A fresh edit session starts with snap on again (per-session toggle, M2-RF-441).
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.IsSnapEnabled);
    }

    [Fact]
    public void RemoveSelectedWidget_RemovesFromThePageAndConfigurationAndMarksDirty()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widget);

        Assert.True(context.Service.RemoveSelectedWidget());

        Assert.Empty(context.Page.Widgets);
        Assert.Empty(context.Config.Pages[0].Widgets);
        Assert.Null(context.Service.SelectedWidget);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void RemoveSelectedWidget_OutsideEditModeOrWithoutSelection_IsNoOp()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));

        // No edit mode yet.
        Assert.False(context.Service.RemoveSelectedWidget());

        context.Service.Enter(1000, 500);
        // Edit mode but nothing selected.
        Assert.False(context.Service.RemoveSelectedWidget());

        Assert.Single(context.Page.Widgets);
        Assert.Single(context.Config.Pages[0].Widgets);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void RequestRemoveSelectedWidget_OpensConfirmationWithoutRemovingOrDirtying()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widget);

        Assert.True(context.Service.RequestRemoveSelectedWidget());

        Assert.True(context.Service.IsDeleteConfirmationVisible);
        Assert.Single(context.Page.Widgets);
        Assert.Single(context.Config.Pages[0].Widgets);
        Assert.Same(context.Widget, context.Service.SelectedWidget);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void RequestRemoveSelectedWidget_OutsideEditModeOrWithoutSelection_IsNoOp()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));

        Assert.False(context.Service.RequestRemoveSelectedWidget());
        Assert.False(context.Service.IsDeleteConfirmationVisible);

        context.Service.Enter(1000, 500);
        Assert.False(context.Service.RequestRemoveSelectedWidget());
        Assert.False(context.Service.IsDeleteConfirmationVisible);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void CancelRemove_KeepsWidgetSelectionAndDirtyState()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widget);
        context.Service.RequestRemoveSelectedWidget();

        context.Service.CancelRemove();

        Assert.False(context.Service.IsDeleteConfirmationVisible);
        Assert.Single(context.Page.Widgets);
        Assert.Single(context.Config.Pages[0].Widgets);
        Assert.Same(context.Widget, context.Service.SelectedWidget);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void ConfirmRemove_RemovesOnceAndMarksDirty()
    {
        var context = CreateContext(Label("w1", 10, 10, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widget);
        context.Service.RequestRemoveSelectedWidget();

        Assert.True(context.Service.ConfirmRemove());

        Assert.False(context.Service.IsDeleteConfirmationVisible);
        Assert.Empty(context.Page.Widgets);
        Assert.Empty(context.Config.Pages[0].Widgets);
        Assert.True(context.Service.IsDirty);

        // The confirmation is closed, so a second confirm does nothing.
        Assert.False(context.Service.ConfirmRemove());
    }

    [Fact]
    public void ConfirmRemove_WhenTheSelectionChanged_CancelsWithoutDeleting()
    {
        var context = CreateContext(
            Label("w1", 0, 0, 200, 100),
            Label("w2", 0, 200, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widgets[0]);
        context.Service.RequestRemoveSelectedWidget();

        // Selecting another widget cancels the confirmation opened for the previous selection.
        context.Service.SelectWidget(context.Widgets[1]);
        Assert.False(context.Service.IsDeleteConfirmationVisible);
        Assert.False(context.Service.ConfirmRemove());

        Assert.Equal(2, context.Page.Widgets.Count);
        Assert.Equal(2, context.Config.Pages[0].Widgets.Count);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public async Task ConfirmRemove_ExitAsync_PersistsTheRemovalThroughTheStore()
    {
        var context = CreateContext(
            Label("w1", 0, 0, 200, 100),
            Label("w2", 0, 200, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widgets[0]);
        context.Service.RequestRemoveSelectedWidget();
        Assert.True(context.Service.ConfirmRemove());

        var result = await context.Service.ExitAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, context.Store.SaveCallCount);
        var saved = Assert.Single(context.Store.LastSavedConfig!.Pages[0].Widgets);
        Assert.Equal("w2", saved.Id);
    }

    [Fact]
    public void DeleteAffordance_NeverExecutesTheWidgetAction()
    {
        // M5-RF-504/M2-RF-439: the X and its confirmation own the tap; they must never fire the
        // widget's bound action (edit mode owns taps).
        var executor = new RecordingActionExecutor();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext { ActionExecutor = executor });
        var config = ConfigWith(new ButtonWidgetConfig
        {
            Id = "b1",
            X = 0,
            Y = 0,
            Width = 160,
            Height = 120,
            Action = new MuteActionConfig(),
            Label = "Mute",
        });
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        service.Enter(1000, 500);
        service.SelectWidget(page.Widgets[0]);

        Assert.True(service.RequestRemoveSelectedWidget());
        Assert.Equal(0, executor.InvocationCount);

        Assert.True(service.ConfirmRemove());
        Assert.Equal(0, executor.InvocationCount);
    }

    [Fact]
    public void DuplicateSelectedWidget_AddsAnOffsetCopyWithANewIdAndSelectsIt()
    {
        var context = CreateContext(Label("w1", 100, 100, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widget);

        Assert.True(context.Service.DuplicateSelectedWidget());

        Assert.Equal(2, context.Page.Widgets.Count);
        Assert.Equal(2, context.Config.Pages[0].Widgets.Count);

        var copy = context.Page.Widgets[1];
        Assert.Equal("w1-copy", copy.WidgetId);
        Assert.Equal(116, copy.X);
        Assert.Equal(116, copy.Y);
        Assert.Same(copy, context.Service.SelectedWidget);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void DuplicateSelectedWidget_GeneratesANewUniqueIdWhenTheCopyNameIsTaken()
    {
        var context = CreateContext(
            Label("w1", 0, 0, 200, 100),
            Label("w1-copy", 0, 200, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widgets[0]);

        Assert.True(context.Service.DuplicateSelectedWidget());

        Assert.Equal("w1-copy-2", context.Page.Widgets[2].WidgetId);
    }

    [Fact]
    public void DuplicateSelectedWidget_WithoutAFactory_IsNoOp()
    {
        var config = ConfigWith(Label("w1", 0, 0, 200, 100));
        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
        var service = new EditModeService(config, page, new RecordingConfigStore());
        service.Enter(1000, 500);
        service.SelectWidget(page.Widgets[0]);

        Assert.False(service.DuplicateSelectedWidget());
        Assert.Single(page.Widgets);
        Assert.False(service.IsDirty);
    }

    [Fact]
    public void DuplicateSelectedWidget_OutsideEditMode_IsNoOp()
    {
        var context = CreateContext(Label("w1", 0, 0, 200, 100));
        context.Service.SelectWidget(context.Widget);

        Assert.False(context.Service.DuplicateSelectedWidget());
        Assert.Single(context.Page.Widgets);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void PaletteItems_ExposeTheBuiltInTypes()
    {
        var context = CreateContext();

        Assert.Equal(
            new[] { "button", "toggle", "label", "gauge", "slider", "sparkline", "clock", "imageButton", "bar", "image" },
            context.Service.PaletteItems.Select(item => item.Type));
    }

    [Fact]
    public void CanModifySelection_TracksEditModeAndSelection()
    {
        var context = CreateContext(Label("w1", 0, 0, 200, 100));

        Assert.False(context.Service.CanModifySelection);

        context.Service.Enter(1000, 500);
        Assert.False(context.Service.CanModifySelection);

        context.Service.SelectWidget(context.Widget);
        Assert.True(context.Service.CanModifySelection);

        context.Service.SelectWidget(null);
        Assert.False(context.Service.CanModifySelection);
    }

    [Fact]
    public void AddWidget_AddsWithDefaultsAtTheFirstFreePositionAndSelectsIt()
    {
        var context = CreateContext();
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.AddWidget(WidgetTypes.Button));

        Assert.Single(context.Page.Widgets);
        Assert.Single(context.Config.Pages[0].Widgets);

        var viewModel = context.Page.Widgets[0];
        var config = context.Config.Pages[0].Widgets[0];
        Assert.IsType<ButtonWidgetViewModel>(viewModel);
        Assert.IsType<ButtonWidgetConfig>(config);
        Assert.Equal(0, viewModel.X);
        Assert.Equal(0, viewModel.Y);
        Assert.Equal(EditModeService.DefaultWidgetWidth, viewModel.Width);
        Assert.Equal(EditModeService.DefaultWidgetHeight, viewModel.Height);
        Assert.IsType<MuteActionConfig>(config.Action);
        Assert.Same(viewModel, context.Service.SelectedWidget);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void AddWidget_UsesTheTypesLargerRegisteredDefaultSize()
    {
        var context = CreateContext();
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.AddWidget(WidgetTypes.Gauge));

        var viewModel = context.Page.Widgets[0];
        Assert.Equal(240, viewModel.Width);
        Assert.Equal(180, viewModel.Height);
    }

    [Fact]
    public void AddWidget_WhenTheOriginIsOccupied_PlacesAtTheNextFreeGridPosition()
    {
        var context = CreateContext(Label("existing", 0, 0, 160, 120));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.AddWidget(WidgetTypes.Button));

        var added = context.Page.Widgets[1];
        Assert.Equal(160, added.X);
        Assert.Equal(0, added.Y);
    }

    [Fact]
    public void AddWidget_GeneratesAUniqueIdWithinThePage()
    {
        var context = CreateContext(Label("label", 0, 0, 160, 120));
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.AddWidget(WidgetTypes.Label));

        Assert.Equal("label-2", context.Page.Widgets[1].WidgetId);
        Assert.Equal("label-2", context.Config.Pages[0].Widgets[1].Id);
    }

    [Fact]
    public void AddWidget_WhenNoPositionFits_CascadesFromTheLastAddedWidget()
    {
        // The page is fully covered by one tall widget, so no grid position is free. The second
        // add falls back to the fixed 16 DIP cascade from the last added widget.
        var context = CreateContext(Label("wall", 0, 0, 160, 300));
        context.Service.Enter(200, 300);

        Assert.True(context.Service.AddWidget(WidgetTypes.Label));
        Assert.Equal(0, context.Page.Widgets[1].X);
        Assert.Equal(0, context.Page.Widgets[1].Y);

        Assert.True(context.Service.AddWidget(WidgetTypes.Label));
        var cascaded = context.Page.Widgets[2];
        Assert.Equal(EditModeService.DuplicateOffset, cascaded.X);
        Assert.Equal(EditModeService.DuplicateOffset, cascaded.Y);
    }

    [Fact]
    public void AddWidget_UnknownType_IsNotAddable()
    {
        var context = CreateContext();
        context.Service.Enter(1000, 500);

        Assert.False(context.Service.AddWidget("heatmap"));
        Assert.Empty(context.Page.Widgets);
        Assert.Empty(context.Config.Pages[0].Widgets);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void AddWidget_OutsideEditMode_IsNoOp()
    {
        var context = CreateContext();

        Assert.False(context.Service.AddWidget(WidgetTypes.Button));
        Assert.Empty(context.Page.Widgets);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public async Task AddWidget_ExitAsync_PersistsTheNewWidgetThroughTheStore()
    {
        var context = CreateContext();
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.AddWidget(WidgetTypes.Button));

        var result = await context.Service.ExitAsync();

        Assert.True(result.Attempted);
        Assert.True(result.Succeeded);
        Assert.Equal(1, context.Store.SaveCallCount);
        var saved = Assert.Single(context.Store.LastSavedConfig!.Pages[0].Widgets);
        Assert.IsType<ButtonWidgetConfig>(saved);
        Assert.Equal(EditModeService.DefaultWidgetWidth, saved.Width);
        Assert.Equal(EditModeService.DefaultWidgetHeight, saved.Height);
    }

    [Fact]
    public void TryFindFirstFreePosition_SkipsOccupiedCellsAndAllowsTouchingEdges()
    {
        var occupied = new[] { new WidgetGeometry(0, 0, 160, 120) };

        Assert.True(EditModeService.TryFindFirstFreePosition(160, 120, 1000, 500, occupied, 8, out var x, out var y));
        Assert.Equal(160, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void TryFindFirstFreePosition_WhenTheWidgetCannotFit_ReturnsFalse()
    {
        var occupied = new[] { new WidgetGeometry(0, 0, 160, 120) };

        Assert.False(EditModeService.TryFindFirstFreePosition(160, 120, 160, 120, occupied, 8, out _, out _));
        Assert.False(EditModeService.TryFindFirstFreePosition(160, 120, 0, 0, [], 8, out _, out _));
    }

    [Fact]
    public void TryFindFirstFreePosition_HostileGridStep_FallsBackToOneDip()
    {
        Assert.True(EditModeService.TryFindFirstFreePosition(
            40,
            40,
            100,
            100,
            [],
            double.NaN,
            out var x,
            out var y));
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    private static TestContext CreateContext(params WidgetConfig[] widgets)
    {
        var config = ConfigWith(widgets);
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var store = new RecordingConfigStore();
        var notifier = new RecordingNotifier();
        var service = new EditModeService(config, page, store, notifier: notifier, factory: registry, palette: registry);
        return new TestContext(config, page, store, notifier, service);
    }

    private static PanelConfig ConfigWith(params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = PageId,
        Pages = [new PageConfig { Id = PageId, Widgets = [.. widgets] }],
    };

    private static LabelWidgetConfig Label(string id, double x, double y, double width, double height) => new()
    {
        Id = id,
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Label = id,
    };

    private static WidgetViewModelBase CreateWidget(string id) =>
        PageViewModel
            .CreateForActivePage(ConfigWith(Label(id, 0, 0, 100, 100)), WidgetRegistry.CreateDefault())
            .Widgets[0];

    private sealed class RecordingNotifier : IUserNotifier
    {
        public List<(string Title, string Message, NotificationSeverity Severity)> Notifications { get; } = [];

        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info) =>
            Notifications.Add((title, message, severity));
    }

    /// <summary>Convenience bundle for the service under test and its collaborators.</summary>
    private sealed record TestContext(
        PanelConfig Config,
        PageViewModel Page,
        RecordingConfigStore Store,
        RecordingNotifier Notifier,
        EditModeService Service)
    {
        /// <summary>Gets the widget view models of the edited page.</summary>
        public IReadOnlyList<WidgetViewModelBase> Widgets => Page.Widgets;

        /// <summary>Gets the first widget view model of the edited page.</summary>
        public WidgetViewModelBase Widget => Page.Widgets[0];
    }
}
