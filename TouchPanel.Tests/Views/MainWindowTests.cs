using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TouchPanel.Config;
using TouchPanel.Interop;
using TouchPanel.Services.Monitors;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Views;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Window-mechanics checks for the panel (T-08, RF-014/RF-018/RF-019/RF-028): the declared
/// borderless/no-activate properties and the extended styles applied in <c>SourceInitialized</c>.
/// The window is created and its HWND ensured, but never shown and never activated, so the test
/// touches no desktop state; full placement/focus behavior is verified manually (MV-01..MV-04).
/// </summary>
public sealed class MainWindowTests
{
    [Fact]
    public async Task SourceInitialized_AppliesNoActivateAndToolWindowExtendedStyles()
    {
        using var sta = new StaThread();

        var snapshot = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var handle = new WindowInteropHelper(window).EnsureHandle();
                var exStyle = WindowStyles.GetWindowLongPtr(handle, WindowStyles.GwlExStyle);

                return new WindowSnapshot(
                    exStyle,
                    window.ShowActivated,
                    window.ShowInTaskbar,
                    window.WindowStyle,
                    window.ResizeMode,
                    window.IsVisible);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        // RF-018/RF-019: both extended styles must be present before the window can be shown.
        Assert.True(((long)snapshot.ExStyle & WindowStyles.WsExNoActivate) != 0);
        Assert.True(((long)snapshot.ExStyle & WindowStyles.WsExToolWindow) != 0);

        // RF-014/RF-018/RF-019: borderless, not resizable, not in the taskbar, never activated.
        Assert.False(snapshot.ShowActivated);
        Assert.False(snapshot.ShowInTaskbar);
        Assert.Equal(WindowStyle.None, snapshot.WindowStyle);
        Assert.Equal(ResizeMode.NoResize, snapshot.ResizeMode);
        Assert.False(snapshot.IsVisible);
    }

    [Fact]
    public async Task CloseRequest_WithoutAllowExit_IsCanceledAndRaisesCloseToTray()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            var closeToTrayRaised = 0;
            window.CloseToTrayRequested += (_, _) => closeToTrayRaised++;

            try
            {
                new WindowInteropHelper(window).EnsureHandle();

                // A close request during normal operation must hide to the tray, not terminate
                // (RF-006); the composition root owns the one-time notification.
                window.Close();

                return (Raised: closeToTrayRaised, IsVisible: window.IsVisible);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Equal(1, result.Raised);
        Assert.False(result.IsVisible);
    }

    [Fact]
    public async Task AttachEditModeService_WiresTheOverlayAndEnterEditModeRequestsActivation()
    {
        // T-12/D-6: the overlay receives the service as its data context, and entering edit mode
        // requests activation exactly once (the only intentional activation path). The actual
        // foreground change is verified manually in MV-06.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
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
                            Widgets = [new LabelWidgetConfig { Id = "l1", X = 0, Y = 0, Width = 200, Height = 40 }],
                        },
                    ],
                };
                var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
                var service = new EditModeService(config, page, new RecordingConfigStore());

                window.AttachEditModeService(service);

                var overlay = Assert.IsType<EditOverlay>(window.FindName("EditOverlay"));
                var attached = ReferenceEquals(overlay.DataContext, service);
                var before = window.EditModeActivationRequestCount;

                window.EnterEditMode();

                return (attached, activations: window.EditModeActivationRequestCount - before);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.True(result.attached);
        Assert.Equal(1, result.activations);
    }

    [Fact]
    public async Task HandleDpiChange_ReassertsPlacementAndRaisesTheChangeEvent()
    {
        // RF-021/RF-022: a live WM_DPICHANGED must re-assert the physical monitor bounds (the
        // hook calls this handler) and tell the composition root to re-evaluate the target.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            var raised = 0;
            window.DpiConfigurationChanged += (_, _) => raised++;
            try
            {
                var monitor = new MonitorDescriptor
                {
                    FriendlyName = "Touchscreen",
                    Bounds = new PixelRect(1920, 0, 1024, 600),
                    WorkArea = new PixelRect(1920, 0, 1024, 560),
                };
                window.PlaceOnMonitor(monitor);

                window.HandleDpiChange();

                return (Raised: raised, Monitor: window.TargetMonitor, IsVisible: window.IsVisible);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Equal(1, result.Raised);
        Assert.NotNull(result.Monitor);
        Assert.Equal(new PixelRect(1920, 0, 1024, 600), result.Monitor!.Bounds);
        Assert.False(result.IsVisible);
    }

    [Fact]
    public async Task DpiScale_AfterHandleCreation_ReturnsAPositiveFiniteScale()
    {
        // RF-022: the display-change handler divides physical monitor bounds by this scale, so it
        // must be usable before the window is shown (hidden placement on a scaled monitor).
        using var sta = new StaThread();

        var scale = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                new WindowInteropHelper(window).EnsureHandle();
                return window.DpiScale;
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.True(double.IsFinite(scale));
        Assert.True(scale > 0);
    }

    [Fact]
    public async Task HandleDpiChange_BeforeAnyPlacement_DoesNotThrowOrShowTheWindow()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                window.HandleDpiChange();
                return (window.TargetMonitor, window.IsVisible);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Null(result.TargetMonitor);
        Assert.False(result.IsVisible);
    }

    [Fact]
    public async Task EditModeKey_EscapeInEditMode_RaisesTheExitRequestAndReportsHandled()
    {
        // T-12/RF-027: Esc while edit mode is active must ask the composition root to exit and
        // consume the key.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (service, _) = AttachEditMode(window);
                service.Enter(1024, 600);

                var raised = 0;
                window.EditModeExitRequested += (_, _) => raised++;

                var handled = window.TryHandleEditModeKey(Key.Escape);

                return (Handled: handled, Raised: raised);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.True(result.Handled);
        Assert.Equal(1, result.Raised);
    }

    [Fact]
    public async Task EditModeKey_ArrowKeysInEditMode_NudgeByExactlyOneDipAndReportHandled()
    {
        // T-12/RF-027: every arrow maps to a 1 DIP nudge of the selected widget in its direction.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (service, widget) = AttachEditMode(window);
                service.Enter(1000, 500);
                // Snap off preserves the M1 1 DIP nudge; the snap-on grid nudge (M2-RF-443) is
                // covered by the edit-mode service tests.
                service.ToggleSnap();
                service.SelectWidget(widget);

                (Key Key, bool Handled, double DeltaX, double DeltaY)[] steps =
                [
                    (Key.Right, false, 0, 0),
                    (Key.Left, false, 0, 0),
                    (Key.Down, false, 0, 0),
                    (Key.Up, false, 0, 0),
                ];

                for (var i = 0; i < steps.Length; i++)
                {
                    var beforeX = widget.X;
                    var beforeY = widget.Y;
                    var handled = window.TryHandleEditModeKey(steps[i].Key);
                    steps[i] = (steps[i].Key, handled, widget.X - beforeX, widget.Y - beforeY);
                }

                return (Steps: steps, widget.X, widget.Y);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Equal(4, result.Steps.Length);
        Assert.All(result.Steps, step => Assert.True(step.Handled));

        // Right/Left move horizontally, Down/Up vertically, always one DIP.
        Assert.Equal(1, result.Steps[0].DeltaX);
        Assert.Equal(0, result.Steps[0].DeltaY);
        Assert.Equal(-1, result.Steps[1].DeltaX);
        Assert.Equal(0, result.Steps[1].DeltaY);
        Assert.Equal(0, result.Steps[2].DeltaX);
        Assert.Equal(1, result.Steps[2].DeltaY);
        Assert.Equal(0, result.Steps[3].DeltaX);
        Assert.Equal(-1, result.Steps[3].DeltaY);

        // The four nudges cancel out: the widget is back at its configured position.
        Assert.Equal(100, result.X);
        Assert.Equal(100, result.Y);
    }

    [Fact]
    public async Task EditModeKey_ArrowKeysAtTheEdge_ReportUnhandledAndKeepTheGeometry()
    {
        // T-12/RF-027: a nudge that the edge clamp cannot apply must not consume the key.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (service, widget) = AttachEditMode(window, x: 0, y: 0);
                service.Enter(1000, 500);
                service.SelectWidget(widget);

                var left = window.TryHandleEditModeKey(Key.Left);
                var up = window.TryHandleEditModeKey(Key.Up);

                return (Left: left, Up: up, X: widget.X, Y: widget.Y);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(result.Left);
        Assert.False(result.Up);
        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
    }

    [Fact]
    public async Task EditModeKey_OutsideEditMode_AreIgnored()
    {
        // T-12/RF-025: with the service attached but edit mode off, no key is routed or consumed.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (_, widget) = AttachEditMode(window);

                var raised = 0;
                window.EditModeExitRequested += (_, _) => raised++;

                var escape = window.TryHandleEditModeKey(Key.Escape);
                var right = window.TryHandleEditModeKey(Key.Right);

                return (Escape: escape, Right: right, Raised: raised, X: widget.X, Y: widget.Y);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(result.Escape);
        Assert.False(result.Right);
        Assert.Equal(0, result.Raised);
        Assert.Equal(100, result.X);
        Assert.Equal(100, result.Y);
    }

    [Fact]
    public async Task EditModeKey_WithoutAnEditModeService_AreIgnored()
    {
        // T-12: the panel without a bound edit-mode service never consumes keys or raises exit.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var raised = 0;
                window.EditModeExitRequested += (_, _) => raised++;

                return (
                    Escape: window.TryHandleEditModeKey(Key.Escape),
                    Right: window.TryHandleEditModeKey(Key.Right),
                    Raised: raised);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(result.Escape);
        Assert.False(result.Right);
        Assert.Equal(0, result.Raised);
    }

    [Fact]
    public async Task EditModeKey_DeleteShortcut_RemovesTheSelectedWidgetInEditMode()
    {
        // RF-060/M2-RF-603: Delete is the fixed remove shortcut while edit mode is active.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (service, widget) = AttachEditMode(window);
                service.Enter(1000, 500);
                service.SelectWidget(widget);

                var handled = window.TryHandleEditModeKey(Key.Delete, ModifierKeys.None);
                return (Handled: handled, Count: service.Widgets.Count);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.True(result.Handled);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task EditModeKey_DuplicateShortcut_DuplicatesTheSelectedWidgetInEditMode()
    {
        // RF-060/M2-RF-603: Ctrl+D duplicates the selection with a fixed offset.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (service, widget) = AttachEditMode(window);
                service.Enter(1000, 500);
                service.SelectWidget(widget);

                var handled = window.TryHandleEditModeKey(Key.D, ModifierKeys.Control);
                var copy = service.Widgets[^1];
                return (Handled: handled, Count: service.Widgets.Count, Id: copy.WidgetId, X: copy.X);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.True(result.Handled);
        Assert.Equal(2, result.Count);
        Assert.Equal("l1-copy", result.Id);
        Assert.Equal(116, result.X);
    }

    [Fact]
    public async Task EditModeKey_ToggleSnapShortcut_TogglesSnapInEditMode()
    {
        // RF-060/M2-RF-603: Ctrl+G toggles the edit-session grid snap.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (service, _) = AttachEditMode(window);
                service.Enter(1000, 500);
                var before = service.IsSnapEnabled;

                var handled = window.TryHandleEditModeKey(Key.G, ModifierKeys.Control);
                var after = service.IsSnapEnabled;

                return (Handled: handled, Before: before, After: after);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.True(result.Handled);
        Assert.True(result.Before);
        Assert.False(result.After);
    }

    [Fact]
    public async Task EditModeKey_NewShortcuts_OutsideEditMode_AreIgnoredAndDoNotActivate()
    {
        // RF-060/M2-RF-602/604: outside edit mode the local shortcuts are inert and never take
        // focus; the panel performs no activation as a side effect.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (service, _) = AttachEditMode(window);
                var activationsBefore = window.EditModeActivationRequestCount;

                var remove = window.TryHandleEditModeKey(Key.Delete, ModifierKeys.None);
                var duplicate = window.TryHandleEditModeKey(Key.D, ModifierKeys.Control);
                var snap = window.TryHandleEditModeKey(Key.G, ModifierKeys.Control);

                return (
                    Remove: remove,
                    Duplicate: duplicate,
                    Snap: snap,
                    Activations: window.EditModeActivationRequestCount - activationsBefore,
                    Count: service.Widgets.Count,
                    SnapEnabled: service.IsSnapEnabled);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(result.Remove);
        Assert.False(result.Duplicate);
        Assert.False(result.Snap);
        Assert.Equal(0, result.Activations);
        Assert.Equal(1, result.Count);
        Assert.True(result.SnapEnabled);
    }

    [Fact]
    public async Task EditOverlayExitButton_ForwardsToTheEditModeExitRequest()
    {
        // UX fix #2: the toolbar's Exit Edit Mode button must reach the composition root through the
        // same window event as Esc, so both share the save + non-activating-restore path.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var (service, _) = AttachEditMode(window);
                service.Enter(1024, 600);

                var raised = 0;
                window.EditModeExitRequested += (_, _) => raised++;

                var overlay = Assert.IsType<EditOverlay>(window.FindName("EditOverlay"));
                var button = Assert.IsType<Button>(overlay.FindName("ExitEditModeButton"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                return (Raised: raised, Content: button.Content as string, Focusable: button.Focusable);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Equal(1, result.Raised);
        Assert.Equal("Exit Edit Mode", result.Content);
        Assert.False(result.Focusable, "The Exit Edit Mode button must not pull keyboard focus.");
    }

    private static (EditModeService Service, WidgetViewModelBase Widget) AttachEditMode(
        MainWindow window,
        double x = 100,
        double y = 100)
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
                    Widgets = [new LabelWidgetConfig { Id = "l1", X = x, Y = y, Width = 200, Height = 40 }],
                },
            ],
        };
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry);

        window.AttachEditModeService(service);
        return (service, page.Widgets[0]);
    }

    private readonly record struct WindowSnapshot(
        nint ExStyle,
        bool ShowActivated,
        bool ShowInTaskbar,
        WindowStyle WindowStyle,
        ResizeMode ResizeMode,
        bool IsVisible);
}
