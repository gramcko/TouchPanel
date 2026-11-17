using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Input;
using TouchPanel.Interop;
using TouchPanel.Services.Display;
using TouchPanel.Services.Monitors;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Paging;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views;

namespace TouchPanel;

/// <summary>
/// The TouchPanel panel window (RF-014/RF-018/RF-019/RF-028): borderless, sized to the target
/// monitor's full physical bounds, absent from the taskbar and Alt+Tab, and never activating
/// itself in normal mode. The code-behind contains only window mechanics — ex-styles, placement,
/// show/hide, topmost, and close-to-tray (plan section 1.2); all policy decisions live in
/// <c>ViewModels.MainWindowViewModel</c> and the composition root.
/// </summary>
/// <remarks>
/// <para><b>Focus preservation (RF-018).</b> The window is declared with
/// <c>ShowActivated=false</c> and receives <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> when its
/// HWND is created; placement, show/hide, and topmost changes all use the no-activate Win32
/// paths. Nothing in this class calls <c>Activate()</c> or <c>SetForegroundWindow</c>.</para>
/// <para><b>Physical pixels (DD-5).</b> Bounds come from <see cref="MonitorDescriptor.Bounds"/>
/// (<c>rcMonitor</c>) and are applied with <c>SetWindowPos</c>; WPF's DIP conversion is never
/// involved, which avoids the PerMonitorV2 ambiguity tracked by R-4.</para>
/// <para><b>Close to tray (RF-006).</b> A close request hides the window and keeps the process
/// alive; only the explicit shutdown path sets <see cref="AllowExit"/>.</para>
/// </remarks>
public partial class MainWindow : Window, IPanelWindow
{
    private nint _handle;
    private MonitorDescriptor? _targetMonitor;
    private HwndSource? _hwndSource;
    private HwndSourceHook? _windowHook;
    private EditModeService? _editModeService;
    private PageTransitionController? _pageTransition;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class and subscribes to
    /// <see cref="Window.SourceInitialized"/> so the no-activate extended styles are applied
    /// before the window can ever be shown.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;

        // UX fix #1: a long press on a widget in normal mode requests edit-mode entry. The window
        // only forwards the request; the composition root owns the deliberate activation (D-6).
        PageHost.EditModeRequested += OnPageHostEditModeRequested;

        // UX fix #2: the edit toolbar's Exit Edit Mode button requests an exit. Forward it through
        // the same event as Esc so the composition root runs the single save/restore path.
        EditOverlay.EditModeExitRequested += OnEditOverlayExitRequested;
    }

    /// <summary>
    /// Gets or sets a value indicating whether a close request may actually close the window.
    /// It stays <see langword="false"/> during normal operation, so Alt+F4/WM_CLOSE hides the
    /// panel to the tray (RF-006); the composition root sets it to <see langword="true"/> before
    /// an explicit shutdown.
    /// </summary>
    public bool AllowExit { get; set; }

    /// <summary>Gets the monitor whose full bounds the panel targets, or <see langword="null"/> before placement.</summary>
    public MonitorDescriptor? TargetMonitor => _targetMonitor;

    /// <summary>
    /// Gets or sets the normal-mode page-change animation (user-requested addition, additive under
    /// schema v4). The composition root applies it from <c>window.pageTransition</c> at startup and
    /// live through the shared apply pipeline; a change takes effect on the next page change without
    /// a restart.
    /// </summary>
    public PageTransitionKind PageTransition { get; set; } = PageTransitionKind.Slide;

    /// <summary>
    /// Gets or sets the context gate consulted before a transition animates. The composition root
    /// returns <see langword="false"/> while an edit session owns the panel (page changes there
    /// already save/exit/re-enter); <see langword="null"/> means allowed. The OS animation-effects
    /// preference no longer suppresses playback: an explicit mode always wins.
    /// </summary>
    public Func<bool>? PageTransitionsAllowed { get; set; }

    /// <summary>
    /// Gets or sets the optional diagnostic sink for page transitions (one Information-style line
    /// per page change). The composition root supplies it so the settings/planner decision is
    /// visible in the log; the window only forwards the delegate to the controller.
    /// </summary>
    public Action<string>? PageTransitionLog { get; set; }

    /// <summary>Gets the transition controller (created on first use) for the window's STA tests.</summary>
    internal PageTransitionController PageTransitionForTest => Transition;

    private PageTransitionController Transition =>
        _pageTransition ??= new PageTransitionController(
            PageTransitionLayer,
            () => PageTransitionsAllowed?.Invoke() != false,
            log: line => PageTransitionLog?.Invoke(line));

    /// <summary>
    /// Replaces the rendered page and, in normal mode, plays the configured transition
    /// (user-requested addition). The outgoing page is captured before the swap, the incoming page is
    /// interactive immediately, and the animation never runs at startup, in edit mode, or when the
    /// mode is <c>none</c>. If the outgoing snapshot cannot be captured, the selected animation
    /// still plays over the incoming page.
    /// </summary>
    /// <param name="page">The new page view model, or <see langword="null"/> for no widgets.</param>
    /// <param name="direction">The page-change direction (forward for tabs/Open, backward for a right swipe).</param>
    /// <param name="animate">Whether this change may animate (page switches only; never on reload/startup).</param>
    internal void ShowPage(object? page, PageTransitionDirection direction, bool animate)
    {
        var controller = Transition;

        if (!animate)
        {
            controller.Reset();
        }

        var started = animate && controller.BeginTransition(PageHost, PageTransition, direction);

        DataContext = page;
        if (!started)
        {
            return;
        }

        // The new content must be measured before the animation starts, otherwise the slide would
        // begin from the previous layout; UpdateLayout is synchronous and cheap for a page swap.
        PageHost.UpdateLayout();
        controller.CompleteTransition(PageHost);
    }

    /// <summary>
    /// Gets the DPI scale (physical pixels per DIP) of the monitor the panel currently occupies
    /// (RF-021/RF-022), used by the display-change handler to convert the target monitor's
    /// physical bounds into the DIP page bounds widgets are clamped against. Falls back to 1.0
    /// before a presentation source exists or when the scale cannot be read; never throws.
    /// </summary>
    public double DpiScale
    {
        get
        {
            // GetDpiForWindow reports the DPI of the monitor hosting the window from its physical
            // rect, so it is also correct while the window is hidden and was just moved to a
            // monitor with a different scale.
            var handle = _handle;
            if (handle != 0)
            {
                try
                {
                    var dpi = WindowStyles.GetDpiForWindow(handle);
                    if (dpi > 0)
                    {
                        return dpi / 96d;
                    }
                }
                catch (Exception)
                {
                    // Fall through to the WPF visual DPI (for example on an OS without the API).
                }
            }

            try
            {
                var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                return double.IsFinite(scale) && scale > 0 ? scale : 1d;
            }
            catch (Exception)
            {
                // No presentation source yet (for example a window whose HWND creation failed);
                // 1.0 keeps the clamp bounds finite and conservative.
                return 1d;
            }
        }
    }

    /// <summary>
    /// Raised after a close request was canceled and the panel was hidden to the tray (RF-006).
    /// The composition root owns the one-time notification and the runtime-flag persistence.
    /// </summary>
    public event EventHandler? CloseToTrayRequested;

    /// <summary>
    /// Raised on the dispatcher thread after the window received <c>WM_DPICHANGED</c> and the
    /// physical placement was re-asserted (RF-021/RF-022). The composition root subscribes to
    /// re-evaluate the target monitor and apply the RF-017 policy with fresh bounds.
    /// </summary>
    public event EventHandler? DpiConfigurationChanged;

    /// <summary>
    /// Raised on the dispatcher thread when the user pressed Esc while edit mode is active
    /// (RF-027). The composition root owns the exit (save + restoring the tray state); this window
    /// only translates the key.
    /// </summary>
    public event EventHandler? EditModeExitRequested;

    /// <summary>
    /// Raised on the dispatcher thread when a widget is long-pressed in normal mode (UX fix #1). The
    /// composition root owns the entry (show if needed + the deliberate activation allowed by D-6);
    /// the window only forwards the page host's request.
    /// </summary>
    public event EventHandler? EditModeEnterRequested;

    private void OnPageHostEditModeRequested(object? sender, EventArgs e) =>
        EditModeEnterRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Forwards the edit toolbar's Exit Edit Mode request (UX fix #2) to the composition root through
    /// <see cref="EditModeExitRequested"/>, exactly like the Esc shortcut: the window never exits
    /// edit mode itself, so the save and non-activating restore stay owned by the composition root.
    /// </summary>
    private void OnEditOverlayExitRequested(object? sender, EventArgs e) =>
        EditModeExitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Gets the number of times entering edit mode requested activation through
    /// <see cref="EnterEditMode"/> (D-6). Exposed for the window-mechanics tests; the actual
    /// foreground change is verified manually in T-14/MV-06.
    /// </summary>
    internal int EditModeActivationRequestCount { get; private set; }

    /// <summary>
    /// Records the target monitor and applies its full physical bounds (<c>rcMonitor</c>) with
    /// <c>SetWindowPos</c> and <c>SWP_NOACTIVATE</c> (RF-014/RF-028, DD-5). Safe to call before
    /// the window is shown; <see cref="ShowWithoutActivation"/> re-asserts the placement after
    /// WPF's show-time layout.
    /// </summary>
    /// <param name="monitor">The resolved target monitor; must carry non-empty bounds.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="monitor"/> is <see langword="null"/>.</exception>
    public void PlaceOnMonitor(MonitorDescriptor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        _targetMonitor = monitor;
        ApplyPlacement();
    }

    /// <summary>
    /// Shows the panel without activating it (RF-018/RF-024). WPF's <see cref="Window.Show"/>
    /// honors <c>ShowActivated=false</c>, the physical placement is re-asserted after WPF's
    /// show-time layout, and the native show uses <c>SW_SHOWNOACTIVATE</c> (DD-5). Never calls
    /// <c>Activate</c> or <c>SetForegroundWindow</c>.
    /// </summary>
    public void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            Show();
        }

        ApplyPlacement();

        var handle = EnsureHandle();
        if (handle != 0)
        {
            WindowStyles.ShowWindow(handle, WindowStyles.SwShowNoActivate);
        }
    }

    /// <summary>
    /// Hides the panel through WPF's visibility bookkeeping and the native <c>SW_HIDE</c>
    /// (RF-006/RF-024, DD-5). Never activates anything.
    /// </summary>
    public void HidePanel()
    {
        if (IsVisible)
        {
            Hide();
        }

        if (_handle != 0)
        {
            WindowStyles.ShowWindow(_handle, WindowStyles.SwHide);
        }
    }

    /// <summary>
    /// Applies the configured always-on-top behavior without activation (RF-020): moves the
    /// window into or out of the topmost band with <c>HWND_TOPMOST</c>/<c>HWND_NOTOPMOST</c>,
    /// <c>SWP_NOACTIVATE</c>, and no position or size change.
    /// </summary>
    /// <param name="topmost">Whether the panel should stay above non-topmost windows.</param>
    public void SetTopmost(bool topmost)
    {
        var handle = EnsureHandle();
        if (handle == 0)
        {
            return;
        }

        WindowStyles.SetWindowPos(
            handle,
            topmost ? WindowStyles.HwndTopmost : WindowStyles.HwndNotTopmost,
            0,
            0,
            0,
            0,
            WindowStyles.SwpNoMove | WindowStyles.SwpNoSize | WindowStyles.SwpNoActivate);
    }

    /// <summary>
    /// Binds the edit-mode service to the overlay (T-12/DD-7) so its widget list, selection state,
    /// and edit-mode flag drive the overlay's ItemsControl. Must be called on the UI thread after
    /// the configuration load created the service.
    /// </summary>
    /// <param name="editModeService">The service bound to the loaded configuration and active page.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editModeService"/> is <see langword="null"/>.</exception>
    public void AttachEditModeService(EditModeService editModeService)
    {
        ArgumentNullException.ThrowIfNull(editModeService);

        _editModeService = editModeService;
        EditOverlay.DataContext = editModeService;
    }

    /// <summary>
    /// Binds the in-panel page selector to its view model (T-16/RF-091) so its touch tabs can switch
    /// the active page. Must be called on the UI thread after the configuration load built the
    /// selector view model. Selecting a page never activates the window (M2-RF-913).
    /// </summary>
    /// <param name="viewModel">The page-selector presentation state; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public void AttachPageSwitcher(PageSwitcherViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        PageSwitcher.DataContext = viewModel;

        // The pager's swipe/drag gestures are inert while edit mode owns the pointer, so the edit
        // overlay's drag/resize handles keep their own gestures; page switches are likewise refused
        // by the coordinator during an edit session.
        PageSwitcher.IsPagingEnabled = () => _editModeService?.IsEditMode != true;
    }

    /// <summary>
    /// Binds the inline pages editor shown by the edit toolbar (user-requested page management inside
    /// edit mode). The view model is supplied by the composition root and steps out of and back into
    /// the edit session around page changes; the window only forwards it to the overlay so no page
    /// policy lives in the view.
    /// </summary>
    /// <param name="pagesEditor">The pages-management presentation state; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pagesEditor"/> is <see langword="null"/>.</exception>
    public void AttachPagesEditor(PagesEditorViewModel pagesEditor)
    {
        ArgumentNullException.ThrowIfNull(pagesEditor);

        EditOverlay.AttachPagesEditor(pagesEditor);
    }

    /// <summary>
    /// Applies the deliberate activation allowed when entering edit mode (D-6/RF-026). This is the
    /// only code path in the application that may take focus, and it runs only for an explicit
    /// user action (tray toggle); normal mode never calls <c>Activate</c> (RF-018). Activation is
    /// best-effort: the OS can refuse a foreground change, in which case touch editing still works.
    /// </summary>
    public void EnterEditMode()
    {
        EditModeActivationRequestCount++;

        try
        {
            Activate();
        }
        catch (Exception)
        {
            // Never let a refused foreground change break editing.
        }
    }

    /// <summary>
    /// Edit-mode keyboard behavior (RF-027/RF-060): routes the fixed M2 local shortcut set (Esc exit,
    /// arrow nudge, Delete remove, Ctrl+D duplicate, Ctrl+G toggle snap, Ctrl+Z undo, Ctrl+Y redo)
    /// that <see cref="LocalShortcuts"/> resolves. Keys are ignored entirely outside edit mode, so
    /// normal operation stays keyboard-independent and no keyboard focus is required
    /// (RF-025/M2-RF-602); the resolved command never activates the panel (M2-RF-604). The routing
    /// itself lives in <see cref="TryHandleEditModeKey(Key)"/> so it stays testable without
    /// synthesizing key events.
    /// </summary>
    /// <param name="e">The key event data.</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (TryHandleEditModeKey(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Routes an edit-mode key command and reports whether it was consumed. This overload exists for
    /// callers that already know the modifier state (the preview-key handler); the single-argument
    /// overload is used by tests and treats the gesture as unmodified.
    /// </summary>
    /// <param name="key">The pressed key to route.</param>
    /// <param name="modifiers">The modifier state held while the key was pressed.</param>
    /// <returns><see langword="true"/> when the key was consumed; otherwise <see langword="false"/>.</returns>
    internal bool TryHandleEditModeKey(Key key, ModifierKeys modifiers)
    {
        var service = _editModeService;
        if (service is null || !service.IsEditMode)
        {
            // M2-RF-602/M2-RF-604: local shortcuts are inert outside edit mode, so normal operation
            // never depends on them and never changes focus.
            return false;
        }

        if (!TryMapLocalShortcutKey(key, out var localKey)
            || !LocalShortcuts.TryResolve(localKey, MapLocalShortcutModifiers(modifiers), out var shortcut))
        {
            return false;
        }

        return ApplyLocalShortcut(service, shortcut);
    }

    /// <summary>
    /// Routes an unmodified edit-mode key command (the M1 Esc/arrow keys and Delete). Kept for
    /// existing callers; equivalent to passing <see cref="ModifierKeys.None"/> to the modifier-aware
    /// overload.
    /// </summary>
    /// <param name="key">The pressed key to route.</param>
    /// <returns><see langword="true"/> when the key was consumed; otherwise <see langword="false"/>.</returns>
    internal bool TryHandleEditModeKey(Key key) => TryHandleEditModeKey(key, ModifierKeys.None);

    private bool ApplyLocalShortcut(EditModeService service, LocalShortcut shortcut)
    {
        switch (shortcut)
        {
            case LocalShortcut.ExitEditMode:
                EditModeExitRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case LocalShortcut.NudgeLeft:
                return service.NudgeSelectedWidget(-1, 0);
            case LocalShortcut.NudgeRight:
                return service.NudgeSelectedWidget(1, 0);
            case LocalShortcut.NudgeUp:
                return service.NudgeSelectedWidget(0, -1);
            case LocalShortcut.NudgeDown:
                return service.NudgeSelectedWidget(0, 1);
            case LocalShortcut.RemoveSelectedWidget:
                return service.RemoveSelectedWidget();
            case LocalShortcut.DuplicateSelectedWidget:
                return service.DuplicateSelectedWidget();
            case LocalShortcut.ToggleSnap:
                return service.ToggleSnap();
            case LocalShortcut.Undo:
                return service.TryUndo();
            case LocalShortcut.Redo:
                return service.TryRedo();
            default:
                return false;
        }
    }

    /// <summary>
    /// Maps a WPF key onto the neutral local-shortcut key identity; unmapped keys are not part of
    /// the fixed set. Only <c>System.Windows.Input</c> is needed here, keeping the fixed-set logic
    /// itself WPF-free in <see cref="LocalShortcuts"/>.
    /// </summary>
    private static bool TryMapLocalShortcutKey(Key key, out LocalShortcutKey mapped)
    {
        switch (key)
        {
            case Key.Escape:
                mapped = LocalShortcutKey.Escape;
                return true;
            case Key.Left:
                mapped = LocalShortcutKey.Left;
                return true;
            case Key.Right:
                mapped = LocalShortcutKey.Right;
                return true;
            case Key.Up:
                mapped = LocalShortcutKey.Up;
                return true;
            case Key.Down:
                mapped = LocalShortcutKey.Down;
                return true;
            case Key.Delete:
                mapped = LocalShortcutKey.Delete;
                return true;
            case Key.D:
                mapped = LocalShortcutKey.D;
                return true;
            case Key.G:
                mapped = LocalShortcutKey.G;
                return true;
            case Key.Z:
                mapped = LocalShortcutKey.Z;
                return true;
            case Key.Y:
                mapped = LocalShortcutKey.Y;
                return true;
            default:
                mapped = default;
                return false;
        }
    }

    private static LocalShortcutModifiers MapLocalShortcutModifiers(ModifierKeys modifiers)
    {
        var mapped = LocalShortcutModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            mapped |= LocalShortcutModifiers.Control;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            mapped |= LocalShortcutModifiers.Shift;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            mapped |= LocalShortcutModifiers.Alt;
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            mapped |= LocalShortcutModifiers.Win;
        }

        return mapped;
    }

    /// <summary>
    /// Gets the native window handle, creating the HWND when it does not exist yet. Creating the
    /// HWND raises <see cref="Window.SourceInitialized"/>, so the extended styles are guaranteed
    /// to be applied before the returned handle is used.
    /// </summary>
    /// <returns>The window handle, or 0 when creation failed.</returns>
    internal nint EnsureHandle()
    {
        if (_handle != 0)
        {
            return _handle;
        }

        try
        {
            _handle = new WindowInteropHelper(this).EnsureHandle();
        }
        catch (Exception)
        {
            // The caller treats 0 as "no native window available"; the panel operations then
            // degrade to no-ops instead of taking the application down.
            _handle = 0;
        }

        return _handle;
    }

    /// <summary>
    /// RF-006: a close request (for example Alt+F4) hides the panel to the tray and keeps the
    /// process alive. The window only closes for real when <see cref="AllowExit"/> was set by the
    /// explicit shutdown path.
    /// </summary>
    /// <param name="e">The cancelable close event data.</param>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowExit)
        {
            base.OnClosing(e);
            return;
        }

        // RF-006: cancel the close, hide to the tray, and let the composition root own the
        // one-time notification and the runtime-flag persistence. The base call still raises the
        // public Closing event so future subscribers observe the request.
        e.Cancel = true;
        base.OnClosing(e);
        HidePanel();
        CloseToTrayRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> when the HWND is created (RF-018/
    /// RF-019, DD-5), caches the handle for the placement/show/hide helpers, and hooks window
    /// messages so live DPI changes are re-placed physically.
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _handle = handle;

        if (handle == 0)
        {
            return;
        }

        WindowStyles.AddExtendedStyles(handle, WindowStyles.WsExNoActivate | WindowStyles.WsExToolWindow);

        if (HwndSource.FromHwnd(handle) is { } source)
        {
            _hwndSource = source;
            _windowHook = OnWindowMessage;
            source.AddHook(_windowHook);
        }
    }

    /// <summary>
    /// Window-message hook (RF-022): observes <c>WM_DPICHANGED</c> and defers the physical
    /// re-placement until after WPF processed the message, so the DPI transform is current and
    /// the bounds re-assertion is not overwritten by WPF's suggested rect. The message is never
    /// marked handled; WPF keeps its own DPI behavior.
    /// </summary>
    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WindowStyles.WmDpiChanged)
        {
            try
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(HandleDpiChange));
            }
            catch (Exception)
            {
                // The dispatcher is shutting down; no further layout work is required.
            }
        }

        return 0;
    }

    /// <summary>
    /// Re-asserts the panel's physical monitor bounds after the window's DPI changed (RF-021/
    /// RF-022, DD-5) and raises <see cref="DpiConfigurationChanged"/> so the composition root can
    /// re-evaluate the target monitor with fresh bounds. Internal so the window-mechanics tests
    /// can exercise the path without a real DPI change.
    /// </summary>
    internal void HandleDpiChange()
    {
        ApplyPlacement();
        DpiConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes the window-message hook when the HWND is destroyed so the window and the
    /// <see cref="HwndSource"/> do not keep each other alive after close.
    /// </summary>
    /// <param name="e">Event data (unused).</param>
    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource is { } source && _windowHook is { } hook)
        {
            source.RemoveHook(hook);
        }

        _hwndSource = null;
        _windowHook = null;
        _pageTransition?.Dispose();
        _pageTransition = null;
        base.OnClosed(e);
    }

    private void ApplyPlacement()
    {
        if (_targetMonitor is not { } monitor)
        {
            return;
        }

        var handle = EnsureHandle();
        if (handle == 0)
        {
            return;
        }

        var bounds = monitor.Bounds;
        WindowStyles.SetWindowPos(
            handle,
            0,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            WindowStyles.SwpNoActivate | WindowStyles.SwpNoZOrder);
    }
}
