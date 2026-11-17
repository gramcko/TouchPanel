using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Views;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Undo/redo UI wiring (user-requested): the edit toolbar exposes touch-sized Undo/Redo buttons with
/// the service's CanUndo/CanRedo state, and Ctrl+Z / Ctrl+Y are local edit-mode shortcuts that are
/// inert in normal mode. The XAML is instantiated on an STA thread but never shown, so no desktop
/// state is touched and no widget action can fire.
/// </summary>
public sealed class EditUndoUiTests
{
    [Fact]
    public async Task ToolbarUndoRedoButtons_TrackTheHistoryAndDriveTheService()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var (_, _, service) = CreateService();
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var undo = Assert.IsType<Button>(overlay.FindName("UndoButton"));
            var redo = Assert.IsType<Button>(overlay.FindName("RedoButton"));

            var disabledOutsideEditMode = (!undo.IsEnabled, !redo.IsEnabled);

            service.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var disabledWithoutHistory = (!undo.IsEnabled, !redo.IsEnabled);

            service.AddWidget(WidgetTypes.Button);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var afterEdit = (undo.IsEnabled, redo.IsEnabled);

            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var afterUndo = (undo.IsEnabled, redo.IsEnabled, Count: service.Widgets.Count);

            redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var afterRedo = (undo.IsEnabled, redo.IsEnabled, Count: service.Widgets.Count);

            return (disabledOutsideEditMode, disabledWithoutHistory, afterEdit, afterUndo, afterRedo,
                UndoMinHeight: undo.MinHeight, RedoMinHeight: redo.MinHeight, Focusable: undo.Focusable);
        });

        Assert.Equal((true, true), result.disabledOutsideEditMode);
        Assert.Equal((true, true), result.disabledWithoutHistory);
        Assert.Equal((true, false), result.afterEdit);
        Assert.Equal((false, true, 1), result.afterUndo);
        Assert.Equal((true, false, 2), result.afterRedo);
        Assert.True(result.UndoMinHeight >= 44, "Undo must stay touch-sized.");
        Assert.True(result.RedoMinHeight >= 44, "Redo must stay touch-sized.");
        Assert.False(result.Focusable, "Undo must not pull keyboard focus.");
    }

    [Fact]
    public async Task CtrlUndoRedo_AreInertInNormalMode_AndRouteInEditMode()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (_, page, service) = CreateService();
                window.AttachEditModeService(service);

                // Normal mode: the keys are not part of the local set and nothing happens.
                var normalUndo = window.TryHandleEditModeKey(Key.Z, ModifierKeys.Control);
                var normalRedo = window.TryHandleEditModeKey(Key.Y, ModifierKeys.Control);

                service.Enter(1000, 500);
                service.SelectWidget(page.Widgets[0]);
                service.MoveWidget(page.Widgets[0], 220, 100);

                var undoHandled = window.TryHandleEditModeKey(Key.Z, ModifierKeys.Control);
                var xAfterUndo = service.Widgets[0].X;

                var redoHandled = window.TryHandleEditModeKey(Key.Y, ModifierKeys.Control);

                return (normalUndo, normalRedo, undoHandled, redoHandled, xAfterUndo, XAfterRedo: service.Widgets[0].X);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(result.normalUndo);
        Assert.False(result.normalRedo);
        Assert.True(result.undoHandled);
        Assert.True(result.redoHandled);
        Assert.Equal(100, result.xAfterUndo);
        Assert.Equal(220, result.XAfterRedo);
    }

    private static (PanelConfig Config, PageViewModel Page, EditModeService Service) CreateService()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets = [new LabelWidgetConfig { Id = "l1", X = 100, Y = 100, Width = 200, Height = 40 }],
                },
            ],
        };
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(
            config,
            page,
            new RecordingConfigStore(),
            factory: registry,
            palette: registry,
            pageRebuilder: rebuiltConfig => PageViewModel.CreateForActivePage(rebuiltConfig, registry));
        return (config, page, service);
    }
}
