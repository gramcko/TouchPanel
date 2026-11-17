using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Undo/redo for edit-mode layout changes (user-requested): every committed widget add, remove,
/// duplicate, move, resize, and property/action/appearance/label edit becomes one bounded undo step;
/// undo restores the page configuration and rebuilds the live page while staying in edit mode, redo
/// re-applies it, a new edit clears redo, and the history is per edit session. The tests run without
/// a window, hardware, or the user profile: the page rebuild goes through the same factory pipeline
/// as production, only the host adoption is replaced by the factory-only path.
/// </summary>
public sealed class UndoRedoTests
{
    private const string PageId = "main";
    private const string WidgetId = "w1";

    [Fact]
    public void UndoAfterAdd_RestoresThePageAndRedoReapplies_WhileStayingInEditMode()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        Assert.False(context.Service.CanUndo);
        Assert.False(context.Service.CanRedo);

        Assert.True(context.Service.AddWidget(WidgetTypes.Button));
        Assert.Equal(2, context.Service.Widgets.Count);
        Assert.True(context.Service.CanUndo);
        var liveWidgetsBeforeUndo = context.Service.Widgets;

        Assert.True(context.Service.TryUndo());

        // The configuration and the live page are both restored, and the page is a fresh view model
        // built through the existing factory path (not a mutation of the old widget list).
        Assert.Single(context.Config.Pages[0].Widgets);
        Assert.Single(context.Service.Widgets);
        Assert.NotSame(liveWidgetsBeforeUndo, context.Service.Widgets);
        Assert.True(context.Service.IsEditMode);
        Assert.True(context.Service.Widgets[0].IsEditMode);
        Assert.True(context.Service.CanRedo);
        Assert.Equal(0, context.Service.UndoDepth);

        Assert.True(context.Service.TryRedo());
        Assert.Equal(2, context.Service.Widgets.Count);
        Assert.Equal(2, context.Config.Pages[0].Widgets.Count);
        Assert.True(context.Service.IsEditMode);
        Assert.True(context.Service.Widgets[^1].IsEditMode);
        Assert.Single(context.Config.Pages[0].Widgets, widget => widget is ButtonWidgetConfig);
        Assert.True(context.Service.CanUndo);
        Assert.False(context.Service.CanRedo);
    }

    [Fact]
    public void UndoAfterRemove_RestoresTheWidgetWithItsConfiguration()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100), Label("w2", 300, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[1]);

        Assert.True(context.Service.RemoveSelectedWidget());
        Assert.Single(context.Service.Widgets);

        Assert.True(context.Service.TryUndo());
        Assert.Equal(2, context.Service.Widgets.Count);
        Assert.Equal(2, context.Config.Pages[0].Widgets.Count);
        Assert.Contains(context.Service.Widgets, widget => widget.WidgetId == "w2");

        Assert.True(context.Service.TryRedo());
        Assert.Single(context.Service.Widgets);
        Assert.Single(context.Config.Pages[0].Widgets);
    }

    [Fact]
    public void UndoAfterDuplicate_RestoresTheSingleWidget()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);

        Assert.True(context.Service.DuplicateSelectedWidget());
        Assert.Equal(2, context.Service.Widgets.Count);

        Assert.True(context.Service.TryUndo());
        Assert.Single(context.Service.Widgets);
        Assert.Single(context.Config.Pages[0].Widgets);

        Assert.True(context.Service.TryRedo());
        Assert.Equal(2, context.Service.Widgets.Count);
        Assert.Contains(context.Service.Widgets, widget => widget.WidgetId == $"{WidgetId}-copy");
    }

    [Fact]
    public void UndoAfterMove_RestoresTheGeometryAndRedoReappliesIt()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);

        Assert.True(context.Service.MoveWidget(context.Service.Widgets[0], 120, 80));
        Assert.True(context.Service.TryUndo());

        Assert.Equal(0, context.Service.Widgets[0].X);
        Assert.Equal(0, context.Service.Widgets[0].Y);
        Assert.Equal(0, context.Config.Pages[0].Widgets[0].X);
        Assert.Equal(0, context.Config.Pages[0].Widgets[0].Y);

        Assert.True(context.Service.TryRedo());
        Assert.Equal(120, context.Service.Widgets[0].X);
        Assert.Equal(80, context.Service.Widgets[0].Y);
    }

    [Fact]
    public void UndoAfterResize_RestoresTheExtents()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);

        Assert.True(context.Service.ResizeWidget(context.Service.Widgets[0], 320, 220));
        Assert.Equal(320, context.Service.Widgets[0].Width);

        Assert.True(context.Service.TryUndo());
        Assert.Equal(200, context.Service.Widgets[0].Width);
        Assert.Equal(100, context.Service.Widgets[0].Height);

        Assert.True(context.Service.TryRedo());
        Assert.Equal(320, context.Service.Widgets[0].Width);
        Assert.Equal(220, context.Service.Widgets[0].Height);
    }

    [Fact]
    public void UndoAfterADragGesture_IsOneStepRatherThanOnePerDelta()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SetSnapEnabled(false);
        var widget = context.Service.Widgets[0];

        context.Service.BeginDrag(widget);
        Assert.True(context.Service.MoveWidgetBy(widget, 10, 0));
        Assert.True(context.Service.MoveWidgetBy(widget, 10, 0));
        Assert.True(context.Service.MoveWidgetBy(widget, 10, 0));
        context.Service.EndDrag();

        Assert.Equal(30, widget.X);
        Assert.Equal(1, context.Service.UndoDepth);

        Assert.True(context.Service.TryUndo());
        Assert.Equal(0, context.Service.Widgets[0].X);
        Assert.False(context.Service.CanUndo);

        Assert.True(context.Service.TryRedo());
        Assert.Equal(30, context.Service.Widgets[0].X);
    }

    [Fact]
    public void UndoAfterLabelPropertyEdit_RestoresTheLabel()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        Assert.True(context.Service.OpenPropertyEditor());

        context.Service.PropertyEditor!.LabelText = "Changed";
        Assert.Equal("Changed", context.Config.Pages[0].Widgets[0].Label);

        Assert.True(context.Service.TryUndo());
        Assert.Equal(WidgetId, context.Service.Widgets[0].Label);
        Assert.Equal(WidgetId, context.Config.Pages[0].Widgets[0].Label);

        Assert.True(context.Service.TryRedo());
        Assert.Equal("Changed", context.Service.Widgets[0].Label);
    }

    [Fact]
    public void UndoAfterAppearancePropertyEdit_RestoresTheOverride()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        Assert.True(context.Service.OpenPropertyEditor());

        context.Service.PropertyEditor!.BackgroundText = "#FF102030";
        Assert.Equal("#FF102030", context.Config.Pages[0].Widgets[0].Background);
        Assert.NotNull(context.Service.Widgets[0].BackgroundBrush);

        Assert.True(context.Service.TryUndo());
        Assert.Null(context.Config.Pages[0].Widgets[0].Background);
        Assert.Null(context.Service.Widgets[0].BackgroundBrush);

        Assert.True(context.Service.TryRedo());
        Assert.Equal("#FF102030", context.Config.Pages[0].Widgets[0].Background);
        Assert.NotNull(context.Service.Widgets[0].BackgroundBrush);
    }

    [Fact]
    public void UndoAfterActionEdit_RestoresTheBoundAction()
    {
        var context = CreateContext(new ButtonWidgetConfig
        {
            Id = WidgetId,
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Action = new MuteActionConfig(),
        });
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        Assert.True(context.Service.OpenPropertyEditor());
        Assert.IsType<MuteActionConfig>(context.Config.Pages[0].Widgets[0].Action);

        // The action picker writes the new action into the config entry and then asks the service
        // to project it (same seam the picker uses).
        context.Config.Pages[0].Widgets[0].Action = new MediaKeyActionConfig { Key = MediaKeyKind.Next };
        context.Service.PropertyEditor!.ApplyActionEdit();
        Assert.IsType<MediaKeyActionConfig>(context.Config.Pages[0].Widgets[0].Action);

        Assert.True(context.Service.TryUndo());
        Assert.IsType<MuteActionConfig>(context.Config.Pages[0].Widgets[0].Action);

        Assert.True(context.Service.TryRedo());
        Assert.IsType<MediaKeyActionConfig>(context.Config.Pages[0].Widgets[0].Action);
    }

    [Fact]
    public void NewEditAfterUndo_ClearsTheRedoStack()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        Assert.True(context.Service.MoveWidget(context.Service.Widgets[0], 100, 0));
        Assert.True(context.Service.TryUndo());
        Assert.True(context.Service.CanRedo);

        Assert.True(context.Service.MoveWidget(context.Service.Widgets[0], 0, 50));

        Assert.False(context.Service.CanRedo);
        Assert.False(context.Service.TryRedo());
        Assert.True(context.Service.CanUndo);
    }

    [Fact]
    public void History_IsBoundedToFiftySteps()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SetSnapEnabled(false);

        for (var i = 1; i <= EditHistory.DefaultCapacity + 10; i++)
        {
            Assert.True(context.Service.MoveWidget(context.Service.Widgets[0], i * 4 % 400, 0));
        }

        Assert.Equal(EditHistory.DefaultCapacity, context.Service.UndoDepth);

        var undoCount = 0;
        while (context.Service.TryUndo())
        {
            undoCount++;
        }

        Assert.Equal(EditHistory.DefaultCapacity, undoCount);
        Assert.Equal(0, context.Service.UndoDepth);
        Assert.False(context.Service.CanUndo);
    }

    [Fact]
    public async Task History_IsClearedOnExitAndOnTheNextEntry()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        Assert.True(context.Service.MoveWidget(context.Service.Widgets[0], 40, 0));
        Assert.True(context.Service.CanUndo);

        var exit = await context.Service.ExitAsync();

        Assert.True(exit.Succeeded);
        Assert.False(context.Service.CanUndo);
        Assert.False(context.Service.CanRedo);
        Assert.False(context.Service.TryUndo());

        context.Service.Enter(1000, 500);

        Assert.False(context.Service.CanUndo);
        Assert.False(context.Service.CanRedo);
        Assert.Equal(0, context.Service.UndoDepth);
        Assert.Equal(0, context.Service.RedoDepth);
    }

    [Fact]
    public void UndoAndRedo_AreInertOutsideEditMode()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));

        Assert.False(context.Service.CanUndo);
        Assert.False(context.Service.CanRedo);
        Assert.False(context.Service.TryUndo());
        Assert.False(context.Service.TryRedo());
    }

    [Fact]
    public async Task UndoRestore_MarksTheLayoutDirtyAndIsPersistedByTheExistingSave()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        Assert.True(context.Service.MoveWidget(context.Service.Widgets[0], 240, 0));
        Assert.True(context.Service.TryUndo());
        Assert.True(context.Service.IsDirty);

        var save = await context.Service.SaveAsync();

        Assert.True(save.Attempted);
        Assert.True(save.Succeeded);
        Assert.False(context.Service.IsDirty);
        Assert.Same(context.Config, context.Store.LastSavedConfig);
        Assert.Equal(0, context.Config.Pages[0].Widgets[0].X);
    }

    [Fact]
    public void UndoRestore_PreservesTheSelectionByWidgetId()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100), Label("w2", 300, 0, 200, 100));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[1]);
        Assert.True(context.Service.MoveWidget(context.Service.Widgets[1], 320, 40));

        Assert.True(context.Service.TryUndo());

        Assert.NotNull(context.Service.SelectedWidget);
        Assert.Equal("w2", context.Service.SelectedWidget!.WidgetId);
        Assert.True(context.Service.SelectedWidget.IsSelected);
        Assert.True(context.Service.CanModifySelection);
    }

    [Fact]
    public void UndoRestore_ClearsTheSelectionWhenTheSelectedWidgetNoLongerExists()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.AddWidget(WidgetTypes.Button));
        var added = context.Service.Widgets[^1];
        context.Service.SelectWidget(added);
        context.Service.OpenPropertyEditor();

        Assert.True(context.Service.TryUndo());

        Assert.Null(context.Service.SelectedWidget);
        Assert.False(context.Service.HasPropertyEditor);
        Assert.False(context.Service.CanModifySelection);
    }

    [Fact]
    public void UndoAndRedo_DoNotExecuteAnyWidgetAction()
    {
        var executor = new RecordingActionExecutor();
        var context = CreateContext(
            new WidgetCreationContext { ActionExecutor = executor },
            new ButtonWidgetConfig
            {
                Id = WidgetId,
                X = 0,
                Y = 0,
                Width = 200,
                Height = 100,
                Action = new MuteActionConfig(),
            });
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        Assert.True(context.Service.AddWidget(WidgetTypes.Button));
        Assert.True(context.Service.MoveWidget(context.Service.Widgets[0], 30, 30));

        Assert.True(context.Service.TryUndo());
        Assert.True(context.Service.TryRedo());
        Assert.True(context.Service.TryUndo());

        Assert.Equal(0, executor.InvocationCount);
    }

    [Fact]
    public void HistoryNotifications_AreRaisedForTheUndoAndRedoState()
    {
        var context = CreateContext(Label(WidgetId, 0, 0, 200, 100));
        var raised = new List<string>();
        context.Service.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                raised.Add(e.PropertyName);
            }
        };

        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        context.Service.MoveWidget(context.Service.Widgets[0], 10, 10);
        context.Service.TryUndo();
        context.Service.TryRedo();

        Assert.Contains(nameof(EditModeService.CanUndo), raised);
        Assert.Contains(nameof(EditModeService.CanRedo), raised);
    }

    [Fact]
    public void UndoRestore_KeepsTypedWidgetPropertiesThroughTheRoundTrip()
    {
        var context = CreateContext(new GaugeWidgetConfig
        {
            Id = WidgetId,
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Sensor = "cpu.total",
            Minimum = 0,
            Maximum = 100,
        });
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Service.Widgets[0]);
        Assert.True(context.Service.MoveWidget(context.Service.Widgets[0], 50, 0));
        Assert.True(context.Service.TryUndo());

        var restored = Assert.IsType<GaugeWidgetConfig>(context.Config.Pages[0].Widgets[0]);
        Assert.Equal("cpu.total", restored.Sensor);
        Assert.Equal(100, restored.Maximum);
    }

    private static TestContext CreateContext(params WidgetConfig[] widgets) =>
        CreateContext(new WidgetCreationContext(), widgets);

    private static TestContext CreateContext(WidgetCreationContext creationContext, params WidgetConfig[] widgets)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = PageId,
            Pages = [new PageConfig { Id = PageId, Widgets = [.. widgets] }],
        };
        var registry = WidgetRegistry.CreateDefault(creationContext);
        var page = PageViewModel.CreateForActivePage(config, registry);
        var store = new RecordingConfigStore();
        var service = new EditModeService(
            config,
            page,
            store,
            factory: registry,
            palette: registry,
            pageRebuilder: rebuiltConfig => PageViewModel.CreateForActivePage(rebuiltConfig, registry));
        return new TestContext(config, page, store, service);
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

    /// <summary>Convenience bundle for the service under test and its collaborators.</summary>
    private sealed record TestContext(
        PanelConfig Config,
        PageViewModel Page,
        RecordingConfigStore Store,
        EditModeService Service);
}
