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
                WindowIconAssert.IsPackagedAppIcon(window);

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

    [Fact]
    public async Task EnterEditMode_ClearsNoActivateAndLeaveEditModeRestoresItIdempotently()
    {
        // D-6: typing in the edit overlay needs keyboard focus, so entry is the one deliberate
        // activation path: the no-activate bit is cleared and the window becomes interactive.
        // Leaving restores the bit, and calling leave twice must be safe.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var handle = window.EnsureHandle();
                var initial = ReadExStyle(handle);
                var beforeInteractive = window.IsInteractiveForEditing;

                window.EnterEditMode();
                var interactiveStyle = ReadExStyle(handle);
                var duringInteractive = window.IsInteractiveForEditing;

                window.LeaveEditMode();
                var restoredStyle = ReadExStyle(handle);
                var afterInteractive = window.IsInteractiveForEditing;

                window.LeaveEditMode();
                var secondLeaveStyle = ReadExStyle(handle);

                return (Initial: initial, InteractiveStyle: interactiveStyle, RestoredStyle: restoredStyle, SecondLeaveStyle: secondLeaveStyle, Before: beforeInteractive, During: duringInteractive, After: afterInteractive);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.True((result.Initial & WindowStyles.WsExNoActivate) != 0);
        Assert.False(result.Before);

        // Entry clears only WS_EX_NOACTIVATE; WS_EX_TOOLWINDOW must stay (RF-019).
        Assert.True((result.InteractiveStyle & WindowStyles.WsExNoActivate) == 0);
        Assert.True((result.InteractiveStyle & WindowStyles.WsExToolWindow) != 0);
        Assert.True(result.During);

        // Exit re-applies the bit and keeps every other bit; the second call is a no-op.
        Assert.True((result.RestoredStyle & WindowStyles.WsExNoActivate) != 0);
        Assert.True((result.RestoredStyle & WindowStyles.WsExToolWindow) != 0);
        Assert.False(result.After);
        Assert.Equal(result.RestoredStyle, result.SecondLeaveStyle);
    }

    [Fact]
    public async Task RemoveExtendedStyles_ClearsOnlyTheRequestedBits()
    {
        // D-6: removing WS_EX_NOACTIVATE must preserve WS_EX_TOOLWINDOW (and any other bit).
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var handle = window.EnsureHandle();
                var before = ReadExStyle(handle);

                WindowStyles.RemoveExtendedStyles(handle, WindowStyles.WsExNoActivate);
                var afterNoActivate = ReadExStyle(handle);

                WindowStyles.AddExtendedStyles(handle, WindowStyles.WsExNoActivate);
                WindowStyles.RemoveExtendedStyles(handle, WindowStyles.WsExToolWindow);
                var afterToolWindow = ReadExStyle(handle);

                return (Before: before, AfterNoActivate: afterNoActivate, AfterToolWindow: afterToolWindow);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Equal(result.Before & ~(long)WindowStyles.WsExNoActivate, result.AfterNoActivate);
        Assert.True((result.AfterNoActivate & WindowStyles.WsExToolWindow) != 0);

        // The inverse clear leaves the no-activate bit (which was re-added above) untouched.
        Assert.Equal(result.Before & ~(long)WindowStyles.WsExToolWindow, result.AfterToolWindow);
        Assert.True((result.AfterToolWindow & WindowStyles.WsExNoActivate) != 0);
    }

    [Fact]
    public async Task NormalModeWindowOperations_DoNotMutateTheExtendedStyles()
    {
        // RF-018: placement, topmost, page rendering, and the no-activate show path never touch the
        // activation style. Only the explicit edit-mode entry/exit pair may change it.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var handle = window.EnsureHandle();
                var monitor = new MonitorDescriptor
                {
                    FriendlyName = "Touchscreen",
                    Bounds = new PixelRect(0, 0, 64, 48),
                    WorkArea = new PixelRect(0, 0, 64, 48),
                };

                var initial = ReadExStyle(handle);

                window.PlaceOnMonitor(monitor);
                var afterPlacement = ReadExStyle(handle);

                window.SetTopmost(true);
                var afterTopmost = ReadExStyle(handle);

                window.ShowPage(null, PageTransitionDirection.Forward, animate: false);
                var afterPage = ReadExStyle(handle);

                window.ShowWithoutActivation();
                var afterShow = ReadExStyle(handle);

                window.HidePanel();
                var afterHide = ReadExStyle(handle);

                return (Initial: initial, AfterPlacement: afterPlacement, AfterTopmost: afterTopmost, AfterPage: afterPage, AfterShow: afterShow, AfterHide: afterHide, Interactive: window.IsInteractiveForEditing);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Equal(result.Initial, result.AfterPlacement);

        // SetTopmost legitimately sets WS_EX_TOPMOST; the activation style must not change.
        Assert.True((result.AfterTopmost & WindowStyles.WsExNoActivate) != 0);
        Assert.True((result.AfterTopmost & WindowStyles.WsExToolWindow) != 0);

        Assert.Equal(result.AfterTopmost, result.AfterPage);
        Assert.Equal(result.AfterPage, result.AfterShow);
        Assert.Equal(result.AfterPage, result.AfterHide);
        Assert.True((result.Initial & WindowStyles.WsExNoActivate) != 0);
        Assert.False(result.Interactive);
    }

    [Fact]
    public async Task LeaveEditMode_RestoresThePreviousForegroundOnlyWhenThePanelIsForeground()
    {
        // D-6: ending an edit session hands focus back to the window the user was in, but never
        // steals it when the user already switched somewhere else.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var handle = window.EnsureHandle();
                nint previousWindow = 0x1234;
                var foreground = previousWindow;
                var activations = new List<nint>();

                window.ForegroundWindowSource = () => foreground;
                window.ForegroundWindowActivator = activations.Add;

                window.EnterEditMode();

                // The user switched to another app while editing: no restore may happen.
                foreground = 0x5678;
                window.LeaveEditMode();
                var afterForeignForeground = activations.Count;

                // Re-enter and exit while the panel still owns the foreground: restore the saved one.
                foreground = previousWindow;
                window.EnterEditMode();
                foreground = handle;
                window.LeaveEditMode();

                return (Activations: activations.ToArray(), AfterForeignForeground: afterForeignForeground, Interactive: window.IsInteractiveForEditing);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Equal(0, result.AfterForeignForeground);
        Assert.Single(result.Activations);
        Assert.Equal((nint)0x1234, result.Activations[0]);
        Assert.False(result.Interactive);
    }

    [Fact]
    public async Task LeaveEditMode_WithoutAnActiveSession_NeverRequestsForeground()
    {
        // RF-018: normal mode never activates; leave is a no-op when nothing was entered.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                var handle = window.EnsureHandle();
                var activations = new List<nint>();
                window.ForegroundWindowSource = () => 0x1234;
                window.ForegroundWindowActivator = activations.Add;

                var styleBefore = ReadExStyle(handle);
                window.LeaveEditMode();
                var styleAfter = ReadExStyle(handle);

                // Tapping/placing/showing in normal mode must not request the foreground either.
                window.SetTopmost(false);
                window.PlaceOnMonitor(new MonitorDescriptor
                {
                    FriendlyName = "Touchscreen",
                    Bounds = new PixelRect(0, 0, 32, 32),
                    WorkArea = new PixelRect(0, 0, 32, 32),
                });

                return (Activations: activations.Count, StyleBefore: styleBefore, StyleAfter: styleAfter, StyleAfterNormalOps: ReadExStyle(handle));
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.Equal(0, result.Activations);
        Assert.Equal(result.StyleBefore, result.StyleAfter);
        Assert.Equal(result.StyleBefore, result.StyleAfterNormalOps);
    }

    private static long ReadExStyle(nint handle) =>
        (long)WindowStyles.GetWindowLongPtr(handle, WindowStyles.GwlExStyle);

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
