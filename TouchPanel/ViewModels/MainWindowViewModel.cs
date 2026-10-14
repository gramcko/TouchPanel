using TouchPanel.Config;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;

namespace TouchPanel.ViewModels;

/// <summary>
/// Presentation state and pure policy for the panel window (T-08). It owns the show/hide state,
/// the always-on-top flag, the resolved monitor availability/fallback information, the edit-mode
/// flag forwarded from the attached <see cref="ViewModels.EditModeService"/> (T-12), and the
/// one-time close-to-tray notification gate backed by the persisted <see cref="RuntimeState"/>
/// (RF-006). It deliberately references no WPF types and performs no I/O: the composition root
/// executes the returned decisions and persists the flag.
/// </summary>
/// <remarks>
/// <para><b>Monitor policy (RF-017/D-2).</b> <see cref="RequestShow"/> maps a fresh
/// <see cref="MonitorSelectionResult"/> to a <see cref="PanelShowDecision"/>: an available target
/// shows without a notification; an unavailable target with the primary fallback shows on the
/// primary with a notification; an unavailable target without a fallback stays hidden with a
/// notification. A fallback or refusal is never silent.</para>
/// <para><b>Display changes (RF-022).</b> <see cref="ApplyDisplayChange"/> classifies a fresh
/// selection against the current panel state and returns the window action: when the configured
/// target returns after being the reason the panel was hidden, the panel is shown again
/// automatically; when the target disappears the panel is hidden (fallback <c>none</c>) or moved
/// to the primary fallback with a notification (fallback <c>primary</c>); a resolution/DPI change
/// re-places the visible panel with the fresh bounds. Nothing is placed silently.</para>
/// <para><b>Close notification (RF-006).</b> <see cref="TryConsumeCloseNotification"/> returns
/// <see langword="true"/> exactly once per persisted state, so the "TouchPanel keeps running in
/// the tray" balloon is shown only on the first close and the flag survives restarts.</para>
/// </remarks>
public sealed class MainWindowViewModel
{
    private RuntimeState _runtimeState;
    private EditModeService? _editModeService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    /// <param name="runtimeState">
    /// The persisted runtime flags; defaults to a fresh instance so tests and pre-load states
    /// behave as "notification not yet shown". The composition root replaces it with the loaded
    /// state via <see cref="ApplyRuntimeState"/> once <c>state.json</c> was read.
    /// </param>
    public MainWindowViewModel(RuntimeState? runtimeState = null)
    {
        _runtimeState = runtimeState ?? new RuntimeState();
    }

    /// <summary>Gets a value indicating whether the panel is currently shown (or requested to show).</summary>
    public bool IsPanelVisible { get; private set; }

    /// <summary>Gets a value indicating whether the panel stays above non-topmost windows (RF-020, D-3).</summary>
    public bool AlwaysOnTop { get; private set; }

    /// <summary>Gets a value indicating whether the configured target monitor itself was found (RF-017).</summary>
    public bool IsMonitorAvailable { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the panel is hidden specifically because the configured
    /// target monitor was unavailable (RF-017/D-2), so a later display change can show it
    /// automatically when the monitor returns. A user-requested hide clears this flag and therefore
    /// does not arm an automatic show.
    /// </summary>
    public bool IsHiddenBecauseMonitorUnavailable { get; private set; }

    /// <summary>Gets a value indicating whether the current target is the primary fallback rather than the configured target.</summary>
    public bool UsedFallback { get; private set; }

    /// <summary>Gets the effective fallback policy last seen in a selection result (RF-017).</summary>
    public MonitorFallback FallbackPolicy { get; private set; } = MonitorFallback.None;

    /// <summary>Gets the monitor the panel currently targets, or <see langword="null"/> when none is available.</summary>
    public MonitorDescriptor? TargetMonitor { get; private set; }

    /// <summary>
    /// Gets the edit-mode service once the composition root attached it after the configuration
    /// load (T-12/DD-7), or <see langword="null"/> before that. The service owns the flag, the
    /// dirty tracking, and the save-on-exit path.
    /// </summary>
    public EditModeService? EditModeService => _editModeService;

    /// <summary>
    /// Gets a value indicating whether the panel is currently in edit mode (T-12/RF-026). Once the
    /// service exits, the flag returns to <see langword="false"/> synchronously and the normal
    /// non-activating show behavior is in effect again (RF-018).
    /// </summary>
    public bool IsEditMode => _editModeService?.IsEditMode ?? false;

    /// <summary>
    /// Attaches the edit-mode service created by the composition root once the active page exists,
    /// making the edit-mode flag observable through this view model (T-12).
    /// </summary>
    /// <param name="editModeService">The service bound to the loaded configuration and page.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editModeService"/> is <see langword="null"/>.</exception>
    public void AttachEditModeService(EditModeService editModeService)
    {
        ArgumentNullException.ThrowIfNull(editModeService);

        _editModeService = editModeService;
    }

    /// <summary>
    /// Replaces the runtime-state instance once <c>state.json</c> has been loaded (RF-006/RF-073).
    /// </summary>
    /// <param name="runtimeState">The loaded runtime state; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runtimeState"/> is <see langword="null"/>.</exception>
    public void ApplyRuntimeState(RuntimeState runtimeState)
    {
        ArgumentNullException.ThrowIfNull(runtimeState);

        _runtimeState = runtimeState;
    }

    /// <summary>
    /// Applies the configured window options (RF-020). Startup uses this once the configuration
    /// has loaded; live re-application is M2 hot reload (RF-082) and out of scope.
    /// </summary>
    /// <param name="window">The loaded window section; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="window"/> is <see langword="null"/>.</exception>
    public void ApplyWindowOptions(WindowConfig window)
    {
        ArgumentNullException.ThrowIfNull(window);

        AlwaysOnTop = window.AlwaysOnTop;
    }

    /// <summary>
    /// Updates the always-on-top flag and reports whether it changed, so the caller knows if the
    /// window's Z-order must be re-applied (RF-020).
    /// </summary>
    /// <param name="alwaysOnTop">The new flag value.</param>
    /// <returns><see langword="true"/> when the value changed; <see langword="false"/> when it was already current.</returns>
    public bool SetAlwaysOnTop(bool alwaysOnTop)
    {
        if (AlwaysOnTop == alwaysOnTop)
        {
            return false;
        }

        AlwaysOnTop = alwaysOnTop;
        return true;
    }

    /// <summary>
    /// Records the configured fallback policy (RF-017). Unknown values normalize to
    /// <see cref="MonitorFallback.None"/>, matching the selection service.
    /// </summary>
    /// <param name="monitor">The loaded monitor section; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="monitor"/> is <see langword="null"/>.</exception>
    public void ApplyMonitorPolicy(MonitorConfig monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        FallbackPolicy = monitor.Fallback == MonitorFallback.Primary
            ? MonitorFallback.Primary
            : MonitorFallback.None;
    }

    /// <summary>
    /// Evaluates a show request against a fresh monitor selection (RF-017/RF-024) and returns the
    /// action to take. The decision is pure: it updates the view-model state and the notification
    /// fields, but never touches a window, the configuration, or the state store.
    /// </summary>
    /// <param name="selection">The current selection result; must not be <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="PanelShowOutcome.Show"/> with the monitor to occupy, or
    /// <see cref="PanelShowOutcome.StayHidden"/>; both cases carry a notification when the user
    /// must be told (fallback or refusal), never silent.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selection"/> is <see langword="null"/>.</exception>
    public PanelShowDecision RequestShow(MonitorSelectionResult selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        IsMonitorAvailable = selection.IsAvailable;
        UsedFallback = selection.UsedFallback;
        FallbackPolicy = selection.Fallback;
        TargetMonitor = selection.Monitor;

        if (selection.Monitor is not { } monitor)
        {
            // RF-017/D-2: the refusal hides the panel and arms the auto-show for when the
            // configured monitor returns.
            IsPanelVisible = false;
            IsHiddenBecauseMonitorUnavailable = true;
            return new PanelShowDecision
            {
                Outcome = PanelShowOutcome.StayHidden,
                NotificationTitle = "TouchPanel",
                NotificationMessage =
                    "The configured target monitor is unavailable, so the panel stays hidden. "
                    + "Reconnect the monitor or update the monitor configuration.",
                NotificationSeverity = NotificationSeverity.Warning,
            };
        }

        IsPanelVisible = true;
        IsHiddenBecauseMonitorUnavailable = false;
        return new PanelShowDecision
        {
            Outcome = PanelShowOutcome.Show,
            Monitor = monitor,
            UsedFallback = selection.UsedFallback,
            NotificationTitle = selection.UsedFallback ? "TouchPanel" : null,
            NotificationMessage = selection.UsedFallback
                ? "The configured target monitor is unavailable; showing the panel on the primary monitor instead."
                : null,
            NotificationSeverity = selection.UsedFallback ? NotificationSeverity.Warning : NotificationSeverity.Info,
        };
    }

    /// <summary>
    /// Marks the panel as hidden after a user hide request or a close-to-tray event
    /// (RF-006/RF-024). This is an explicit user action, so it also disarms the automatic
    /// show-on-monitor-return behavior (RF-017).
    /// </summary>
    public void RequestHide()
    {
        IsPanelVisible = false;
        IsHiddenBecauseMonitorUnavailable = false;
    }

    /// <summary>
    /// Classifies a fresh monitor selection against the current panel state after the OS reported a
    /// display-configuration change (RF-022) and returns the window action to execute. Pure: it
    /// updates the policy fields and the show/hide state, but never touches a window, the
    /// configuration, or a notifier.
    /// </summary>
    /// <param name="selection">The selection re-evaluated on demand after the display change; must not be <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="PanelDisplayChangeOutcome.None"/> when nothing relevant changed,
    /// <see cref="PanelDisplayChangeOutcome.Show"/> when the returning configured monitor must be
    /// shown automatically, <see cref="PanelDisplayChangeOutcome.Reposition"/> when a visible panel
    /// must be re-placed with fresh bounds (including the fallback or a resolution change), or
    /// <see cref="PanelDisplayChangeOutcome.Hide"/> when the panel must be hidden. Fallback
    /// placement is always accompanied by a notification (RF-017).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selection"/> is <see langword="null"/>.</exception>
    public PanelDisplayChangeDecision ApplyDisplayChange(MonitorSelectionResult selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var wasVisible = IsPanelVisible;
        var wasUsingFallback = UsedFallback;
        var wasHiddenForUnavailableMonitor = IsHiddenBecauseMonitorUnavailable;

        IsMonitorAvailable = selection.IsAvailable;
        UsedFallback = selection.UsedFallback;
        FallbackPolicy = selection.Fallback;
        TargetMonitor = selection.Monitor;

        // 1) The configured target monitor itself is available.
        if (selection is { IsAvailable: true, Monitor: { } target })
        {
            if (wasVisible)
            {
                // Covers a resolution/DPI change on the target and the configured monitor
                // returning while the panel sits on the primary fallback.
                return new PanelDisplayChangeDecision
                {
                    Outcome = PanelDisplayChangeOutcome.Reposition,
                    Monitor = target,
                    UsedFallback = false,
                    Reason = wasUsingFallback
                        ? $"{selection.Reason}; the configured target is available again, returning from the fallback"
                        : $"{selection.Reason}; the target monitor bounds changed",
                };
            }

            if (wasHiddenForUnavailableMonitor)
            {
                // RF-017: the configured monitor returned; show automatically without activation.
                IsPanelVisible = true;
                IsHiddenBecauseMonitorUnavailable = false;
                return new PanelDisplayChangeDecision
                {
                    Outcome = PanelDisplayChangeOutcome.Show,
                    Monitor = target,
                    UsedFallback = false,
                    Reason = selection.Reason,
                };
            }

            return NoAction("the panel is hidden by user request and the target monitor is available");
        }

        // 2) The configured target is unavailable but the primary fallback provides a monitor.
        if (selection is { UsedFallback: true, Monitor: { } fallbackMonitor })
        {
            if (wasVisible)
            {
                if (wasUsingFallback)
                {
                    // Already on the fallback: only the bounds may have changed.
                    return new PanelDisplayChangeDecision
                    {
                        Outcome = PanelDisplayChangeOutcome.Reposition,
                        Monitor = fallbackMonitor,
                        UsedFallback = true,
                        Reason = $"{selection.Reason}; the fallback monitor bounds changed",
                    };
                }

                // RF-017: a fallback placement is never silent.
                return new PanelDisplayChangeDecision
                {
                    Outcome = PanelDisplayChangeOutcome.Reposition,
                    Monitor = fallbackMonitor,
                    UsedFallback = true,
                    Reason = selection.Reason,
                    NotificationTitle = "TouchPanel",
                    NotificationMessage =
                        "The configured target monitor is unavailable; the panel was moved to the primary monitor as configured.",
                    NotificationSeverity = NotificationSeverity.Warning,
                };
            }

            return NoAction("the panel is hidden and the configured target monitor is still unavailable");
        }

        // 3) No monitor at all.
        if (wasVisible)
        {
            // RF-017/D-2: hide, notify, and arm the auto-show for when the monitor returns.
            IsPanelVisible = false;
            IsHiddenBecauseMonitorUnavailable = true;
            return new PanelDisplayChangeDecision
            {
                Outcome = PanelDisplayChangeOutcome.Hide,
                Reason = selection.Reason,
                NotificationTitle = "TouchPanel",
                NotificationMessage =
                    "The target monitor is no longer available, so the panel was hidden. "
                    + "It will be shown automatically when the monitor returns.",
                NotificationSeverity = NotificationSeverity.Warning,
            };
        }

        return NoAction("the panel is hidden and no monitor is available");
    }

    private static PanelDisplayChangeDecision NoAction(string reason) => new()
    {
        Outcome = PanelDisplayChangeOutcome.None,
        Reason = reason,
    };

    /// <summary>
    /// Consumes the one-time close-to-tray notification flag (RF-006): returns
    /// <see langword="true"/> only on the first call for the persisted state and marks it shown.
    /// The caller persists the mutated <see cref="RuntimeState"/> through <c>StateStore</c> so the
    /// notification is never shown again across restarts.
    /// </summary>
    /// <returns><see langword="true"/> when the notification should be shown now.</returns>
    public bool TryConsumeCloseNotification()
    {
        if (_runtimeState.CloseNotificationShown)
        {
            return false;
        }

        _runtimeState.CloseNotificationShown = true;
        return true;
    }
}
