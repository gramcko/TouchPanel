using TouchPanel.Input;

namespace TouchPanel.ViewModels;

/// <summary>
/// Translates a press-and-hold on empty panel space (the page background, not a widget or an
/// interactive control) into an edit-mode entry request, mirroring the widget long press that
/// already exists. A stationary hold crosses the same ~500 ms threshold and requests edit mode; a
/// tap or a predominantly horizontal drag (a page swipe) does not.
/// </summary>
/// <remarks>
/// The type is WPF-free and owns only the decision, so it is deterministic under unit tests with
/// explicit instants. The view owns the timer and event wiring. The coordinator never activates the
/// panel, never executes a widget action, and never touches the configuration; it only reports that
/// edit mode should be entered through the composition root's deliberate entry path (D-6).
/// </remarks>
public sealed class EmptySpaceLongPressCoordinator
{
    /// <summary>
    /// Horizontal travel in DIPs after which a press is treated as a page swipe and the pending long
    /// press is abandoned. It matches the widget long-press cancellation distance so both gestures
    /// behave identically.
    /// </summary>
    public const double SwipeCancelDistance = 12;

    private readonly Action _enterEditMode;
    private readonly LongPressGate _gate;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmptySpaceLongPressCoordinator"/> class.
    /// </summary>
    /// <param name="enterEditMode">Invoked once when the hold fires; must not be <see langword="null"/>.</param>
    /// <param name="threshold">The hold duration that counts as a long press; defaults to ~500 ms.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="enterEditMode"/> is <see langword="null"/>.</exception>
    public EmptySpaceLongPressCoordinator(Action enterEditMode, TimeSpan? threshold = null)
    {
        _enterEditMode = enterEditMode ?? throw new ArgumentNullException(nameof(enterEditMode));
        _gate = new LongPressGate(threshold);
    }

    /// <summary>Gets the hold duration that counts as a long press.</summary>
    public TimeSpan Threshold => _gate.Threshold;

    /// <summary>Gets a value indicating whether an empty-space press is currently being tracked.</summary>
    public bool IsPressed => _gate.IsPressed;

    /// <summary>Gets a value indicating whether the current press already requested edit mode.</summary>
    public bool LongPressRaised => _gate.LongPressRaised;

    /// <summary>
    /// Starts tracking a press on empty panel space at <paramref name="now"/>, replacing any previous
    /// gesture. The caller must have already established that the press did not land on a widget or
    /// an interactive control.
    /// </summary>
    /// <param name="now">The instant the press began.</param>
    public void Press(DateTimeOffset now) => _gate.Press(now);

    /// <summary>
    /// Reports whether the held press crossed the threshold and requests edit mode once. A long press
    /// fires at most once per gesture; the tracked press is kept until release so a tap can still be
    /// distinguished and consumed.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns><see langword="true"/> when a new edit-mode request was raised on this call.</returns>
    public bool Elapsed(DateTimeOffset now)
    {
        if (!_gate.Elapsed(now))
        {
            return false;
        }

        _enterEditMode();
        return true;
    }

    /// <summary>
    /// Feeds pointer movement from the press origin. A predominantly horizontal drag that is large
    /// enough to be a page swipe abandons the pending long press so a swipe never enters edit mode;
    /// vertical movement keeps the hold intact.
    /// </summary>
    /// <param name="deltaX">Horizontal travel in DIPs since the press.</param>
    /// <param name="deltaY">Vertical travel in DIPs since the press.</param>
    /// <returns><see langword="true"/> when the press was canceled as a horizontal swipe.</returns>
    public bool Move(double deltaX, double deltaY)
    {
        if (!_gate.IsPressed)
        {
            return false;
        }

        if (!IsHorizontalSwipeAwayFromPress(deltaX, deltaY))
        {
            return false;
        }

        Cancel();
        return true;
    }

    /// <summary>
    /// Ends the tracked press. Returns <see langword="true"/> when a long press already fired and the
    /// release must be consumed, so no residual tap or click handler sees it.
    /// </summary>
    /// <returns><see langword="true"/> when the release must be suppressed.</returns>
    public bool Release()
    {
        var raised = _gate.LongPressRaised;
        _gate.Release();
        return raised;
    }

    /// <summary>Abandons the tracked gesture without requesting edit mode or consuming a tap.</summary>
    public void Cancel() => _gate.Cancel();

    /// <summary>
    /// Reports whether pointer travel from a press is a predominantly horizontal drag large enough to
    /// be a page swipe. Shared by the empty-space coordinator and the panel host so both use exactly
    /// the same cancellation policy.
    /// </summary>
    /// <param name="deltaX">Horizontal travel in DIPs.</param>
    /// <param name="deltaY">Vertical travel in DIPs.</param>
    /// <returns><see langword="true"/> when the press should be treated as a horizontal swipe.</returns>
    public static bool IsHorizontalSwipeAwayFromPress(double deltaX, double deltaY) =>
        Math.Abs(deltaX) >= SwipeCancelDistance && Math.Abs(deltaX) > Math.Abs(deltaY);
}
