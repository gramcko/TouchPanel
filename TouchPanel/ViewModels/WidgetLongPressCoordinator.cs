using TouchPanel.Input;

namespace TouchPanel.ViewModels;

/// <summary>
/// Translates the touch/mouse press-and-hold gesture on a normal-mode widget into an edit-mode entry
/// request (UX fix #1). The view owns the actual event wiring and timer; this coordinator owns the
/// decision so it can be unit-tested with explicit instants: a hold longer than
/// <see cref="LongPressGate.DefaultThreshold"/> requests edit mode and suppresses the widget's tap,
/// while a shorter press is a normal tap that keeps executing the widget's action.
/// </summary>
/// <remarks>
/// The coordinator never activates the panel or executes an action itself; it only reports the long
/// press so the composition root can use the same deliberate edit-mode entry path (which may
/// activate, D-6), and reports whether a release must suppress the widget action. This is WPF-free
/// apart from the widget view-model identity it tracks.
/// </remarks>
public sealed class WidgetLongPressCoordinator
{
    private readonly Action<WidgetViewModelBase> _enterEditMode;
    private readonly LongPressGate _gate;
    private WidgetViewModelBase? _pressedWidget;

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetLongPressCoordinator"/> class.
    /// </summary>
    /// <param name="enterEditMode">Invoked once when a long press fires; must not be <see langword="null"/>.</param>
    /// <param name="threshold">The hold duration that counts as a long press; defaults to ~500 ms.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="enterEditMode"/> is <see langword="null"/>.</exception>
    public WidgetLongPressCoordinator(Action<WidgetViewModelBase> enterEditMode, TimeSpan? threshold = null)
    {
        _enterEditMode = enterEditMode ?? throw new ArgumentNullException(nameof(enterEditMode));
        _gate = new LongPressGate(threshold);
    }

    /// <summary>Gets the hold duration that counts as a long press.</summary>
    public TimeSpan Threshold => _gate.Threshold;

    /// <summary>Gets a value indicating whether a widget press is currently being tracked.</summary>
    public bool IsPressed => _pressedWidget is not null;

    /// <summary>
    /// Starts tracking a press on <paramref name="widget"/> at <paramref name="now"/>, replacing any
    /// previous gesture.
    /// </summary>
    /// <param name="widget">The pressed widget; must not be <see langword="null"/>.</param>
    /// <param name="now">The instant the press began.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    public void Press(WidgetViewModelBase widget, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(widget);

        _pressedWidget = widget;
        _gate.Press(now);
    }

    /// <summary>
    /// Reports whether the held press crossed the threshold and requests edit mode once. A long press
    /// fires at most once per gesture; the tracked widget is kept until release so callers can still
    /// suppress the tap.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns><see langword="true"/> when the long press fired on this call.</returns>
    public bool Elapsed(DateTimeOffset now)
    {
        if (_pressedWidget is null || !_gate.Elapsed(now))
        {
            return false;
        }

        _enterEditMode(_pressedWidget);
        return true;
    }

    /// <summary>
    /// Ends the tracked press. Returns <see langword="true"/> when the caller must suppress the
    /// widget's tap action because a long press already fired; a short press returns
    /// <see langword="false"/> so the normal action proceeds.
    /// </summary>
    /// <returns><see langword="true"/> when the tap must be suppressed.</returns>
    public bool Release()
    {
        if (_pressedWidget is null)
        {
            return false;
        }

        var suppressActivation = _gate.LongPressRaised;
        _gate.Release();
        _pressedWidget = null;
        return suppressActivation;
    }

    /// <summary>Abandons the tracked gesture without requesting edit mode or suppressing a tap.</summary>
    public void Cancel()
    {
        _gate.Cancel();
        _pressedWidget = null;
    }
}
