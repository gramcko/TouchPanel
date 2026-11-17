namespace TouchPanel.ViewModels.Paging;

/// <summary>Direction of a completed horizontal page swipe.</summary>
public enum PageSwipeDirection
{
    /// <summary>No page change.</summary>
    None = 0,

    /// <summary>Move to the previous page (finger/mouse dragged to the right).</summary>
    Previous,

    /// <summary>Move to the next page (finger/mouse dragged to the left).</summary>
    Next,
}

/// <summary>
/// Result of one gesture step. <see cref="Handled"/> tells the view that the underlying pointer event
/// must be marked handled: once a horizontal swipe is recognized every remaining move/up is consumed
/// so the widget under the finger never executes its tap action or keeps adjusting a slider.
/// </summary>
/// <param name="Handled">Whether the pointer event must be marked handled.</param>
/// <param name="Navigation">The page change to perform, or <see cref="PageSwipeDirection.None"/>.</param>
public readonly record struct PageGestureOutcome(bool Handled, PageSwipeDirection Navigation)
{
    /// <summary>An event that is not part of a page swipe and must continue to the widgets.</summary>
    public static PageGestureOutcome Ignore { get; } = new(false, PageSwipeDirection.None);

    /// <summary>An event consumed by a recognized swipe without a page change yet.</summary>
    public static PageGestureOutcome Suppress { get; } = new(true, PageSwipeDirection.None);
}

/// <summary>
/// Pure horizontal paging gesture state machine for the phone-homepage-style pager
/// (user-requested pages management). It turns pointer positions into three states: idle, tracking,
/// and a recognized horizontal swipe. A swipe is recognized only once the horizontal travel reaches
/// <see cref="DefaultThreshold"/> DIP and is clearly horizontal (at least
/// <see cref="DefaultDirectionRatio"/> times the vertical travel); vertical and diagonal drags are
/// ignored and, when they become clearly vertical, abandon the gesture so a later horizontal drift
/// cannot fire a page change.
/// </summary>
/// <remarks>
/// The view owns event wiring and the actual navigation; this class owns the threshold/direction
/// policy so it is unit-testable without WPF, a window, or hardware. Exactly one navigation is
/// reported per gesture (on release), which prevents a single long swipe from skipping pages.
/// </remarks>
public sealed class PageGestureTracker
{
    /// <summary>Minimum horizontal travel in DIPs before a swipe is recognized.</summary>
    public const double DefaultThreshold = 72;

    /// <summary>How much larger the horizontal travel must be than the vertical travel.</summary>
    public const double DefaultDirectionRatio = 1.75;

    private readonly double _threshold;
    private readonly double _directionRatio;
    private bool _tracking;
    private bool _recognized;
    private PageSwipeDirection _recognizedDirection = PageSwipeDirection.None;
    private double _startX;
    private double _startY;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageGestureTracker"/> class.
    /// </summary>
    /// <param name="threshold">The horizontal travel threshold in DIPs; hostile values fall back to the default.</param>
    /// <param name="directionRatio">The horizontal-to-vertical ratio; hostile values fall back to the default.</param>
    public PageGestureTracker(double? threshold = null, double? directionRatio = null)
    {
        _threshold = threshold is double requested && double.IsFinite(requested) && requested > 0
            ? requested
            : DefaultThreshold;
        _directionRatio = directionRatio is double ratio && double.IsFinite(ratio) && ratio >= 1
            ? ratio
            : DefaultDirectionRatio;
    }

    /// <summary>Gets a value indicating whether a pointer gesture is currently being tracked.</summary>
    public bool IsTracking => _tracking;

    /// <summary>Gets a value indicating whether the current gesture crossed the swipe threshold.</summary>
    public bool IsSwipeRecognized => _recognized;

    /// <summary>
    /// Starts tracking a gesture at the supplied position. A non-finite position is ignored and the
    /// tracker stays idle, so hostile input can never arm a page change.
    /// </summary>
    /// <param name="x">Horizontal position in DIPs relative to the panel.</param>
    /// <param name="y">Vertical position in DIPs relative to the panel.</param>
    /// <returns><see langword="true"/> when tracking started.</returns>
    public bool Begin(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            Reset();
            return false;
        }

        _startX = x;
        _startY = y;
        _tracking = true;
        _recognized = false;
        _recognizedDirection = PageSwipeDirection.None;
        return true;
    }

    /// <summary>
    /// Feeds one pointer move. Once the horizontal threshold is crossed the outcome is
    /// <see cref="PageGestureOutcome.Suppress"/> for that and every later move, so the widget under
    /// the finger stops receiving the drag. A clearly vertical drag abandons the gesture instead, so
    /// an accidental first vertical movement does not later become a page change.
    /// </summary>
    /// <param name="x">Current horizontal position in DIPs.</param>
    /// <param name="y">Current vertical position in DIPs.</param>
    /// <returns>The step outcome; never <see langword="null"/>.</returns>
    public PageGestureOutcome Move(double x, double y)
    {
        if (!_tracking || !double.IsFinite(x) || !double.IsFinite(y))
        {
            return PageGestureOutcome.Ignore;
        }

        var deltaX = x - _startX;
        var deltaY = y - _startY;

        if (_recognized)
        {
            return PageGestureOutcome.Suppress;
        }

        if (IsHorizontalSwipe(deltaX, deltaY))
        {
            _recognized = true;
            _recognizedDirection = ResolveDirection(deltaX, deltaY, recognized: true);
            return PageGestureOutcome.Suppress;
        }

        if (IsClearlyVertical(deltaX, deltaY))
        {
            Reset();
        }

        return PageGestureOutcome.Ignore;
    }

    /// <summary>
    /// Ends the gesture. A recognized swipe reports exactly one navigation (swipe left → next,
    /// swipe right → previous) and is handled; a tap or ignored drag returns
    /// <see cref="PageGestureOutcome.Ignore"/> so the widget's normal action proceeds.
    /// </summary>
    /// <param name="x">Release horizontal position in DIPs.</param>
    /// <param name="y">Release vertical position in DIPs.</param>
    /// <returns>The final gesture outcome; never <see langword="null"/>.</returns>
    public PageGestureOutcome Complete(double x, double y)
    {
        if (!_tracking)
        {
            return PageGestureOutcome.Ignore;
        }

        var swipe = _recognized;
        var navigation = _recognizedDirection;
        if (!swipe && double.IsFinite(x) && double.IsFinite(y))
        {
            var deltaX = x - _startX;
            var deltaY = y - _startY;
            swipe = TryResolveSwipe(deltaX, deltaY, _threshold, _directionRatio, out navigation);
        }

        Reset();
        return swipe
            ? new PageGestureOutcome(Handled: true, navigation)
            : PageGestureOutcome.Ignore;
    }

    /// <summary>Abandons the current gesture without reporting a page change.</summary>
    public void Cancel() => Reset();

    /// <summary>
    /// Pure threshold/direction decision used by the tracker and by callers that only need the
    /// direction policy: a swipe is horizontal when the absolute horizontal travel reaches
    /// <paramref name="threshold"/> and is at least <paramref name="directionRatio"/> times the
    /// absolute vertical travel. Swiping left is <see cref="PageSwipeDirection.Next"/>, swiping right
    /// is <see cref="PageSwipeDirection.Previous"/>.
    /// </summary>
    /// <param name="deltaX">Horizontal travel in DIPs (negative = left).</param>
    /// <param name="deltaY">Vertical travel in DIPs.</param>
    /// <param name="threshold">The horizontal travel threshold; hostile values fall back to the default.</param>
    /// <param name="directionRatio">The horizontal-to-vertical ratio; hostile values fall back to the default.</param>
    /// <param name="direction">The resolved direction.</param>
    /// <returns><see langword="true"/> when the travel is a horizontal page swipe.</returns>
    public static bool TryResolveSwipe(
        double deltaX,
        double deltaY,
        double threshold,
        double directionRatio,
        out PageSwipeDirection direction)
    {
        direction = PageSwipeDirection.None;
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            return false;
        }

        var effectiveThreshold = double.IsFinite(threshold) && threshold > 0
            ? threshold
            : DefaultThreshold;
        var effectiveRatio = double.IsFinite(directionRatio) && directionRatio >= 1
            ? directionRatio
            : DefaultDirectionRatio;

        var horizontal = Math.Abs(deltaX);
        var vertical = Math.Abs(deltaY);
        if (horizontal < effectiveThreshold || horizontal < vertical * effectiveRatio)
        {
            return false;
        }

        direction = deltaX < 0 ? PageSwipeDirection.Next : PageSwipeDirection.Previous;
        return true;
    }

    private bool IsHorizontalSwipe(double deltaX, double deltaY) =>
        TryResolveSwipe(deltaX, deltaY, _threshold, _directionRatio, out _);

    private bool IsClearlyVertical(double deltaX, double deltaY) =>
        Math.Abs(deltaY) >= _threshold && Math.Abs(deltaY) > Math.Abs(deltaX);

    private static PageSwipeDirection ResolveDirection(double deltaX, double deltaY, bool recognized)
    {
        if (!recognized)
        {
            return PageSwipeDirection.None;
        }

        return deltaX < 0 ? PageSwipeDirection.Next : PageSwipeDirection.Previous;
    }

    private void Reset()
    {
        _tracking = false;
        _recognized = false;
        _recognizedDirection = PageSwipeDirection.None;
    }
}
