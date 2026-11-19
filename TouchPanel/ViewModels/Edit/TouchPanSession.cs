namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// Result of one vertical pan step. <see cref="IsPanning"/> tells the view whether the gesture is
/// now a vertical pan (and therefore must consume the pointer) and <see cref="Offset"/> is the
/// scroll offset to apply, already clamped to the scrollable range.
/// </summary>
/// <param name="IsPanning">Whether the gesture has started panning.</param>
/// <param name="Offset">The target vertical scroll offset in DIPs.</param>
public readonly record struct TouchPanUpdate(bool IsPanning, double Offset)
{
    /// <summary>No pan: the gesture is still a tap or a move below the threshold.</summary>
    public static TouchPanUpdate None { get; } = new(false, 0d);
}

/// <summary>
/// Pure one-contact drag-to-scroll state machine for the vertical panels. It turns a touch contact
/// into a tap (no pan), a vertical pan with a clamped target offset, or nothing (horizontal /
/// diagonal drags, extra contacts, non-finite input).
/// </summary>
/// <remarks>
/// <para>
/// The view owns event wiring, touch capture, and the actual scroll call; this class owns the
/// threshold/direction/clamping policy so it is unit-testable without WPF, a window, or hardware.
/// A pan begins only once the vertical travel passes <see cref="DefaultThreshold"/> DIP and is
/// larger than the horizontal travel, which keeps taps on <c>TextBox</c>/<c>ComboBox</c>/
/// <c>CheckBox</c> working and leaves horizontal gestures (for example a text selection drag) alone.
/// </para>
/// <para>
/// The mapped offset follows the finger: dragging up (negative delta) increases the offset. The
/// target is always clamped to <c>[0, scrollableHeight]</c>, so overscroll at either end simply
/// stops. There are no timers and no inertia; the session is direct and predictable.
/// </para>
/// <para>
/// A second contact invalidates the gesture: panning stops immediately and stays off until every
/// contact has lifted, so multi-touch can never produce a pan. <see cref="Cancel"/> (a lost capture)
/// clears the contacts as well and requires a fresh <see cref="Begin"/>.
/// </para>
/// </remarks>
public sealed class TouchPanSession
{
    /// <summary>Minimum vertical travel in DIPs before a pan starts.</summary>
    public const double DefaultThreshold = 8d;

    private readonly double _threshold;
    private readonly HashSet<long> _contacts = [];
    private bool _tracking;
    private bool _panning;
    private bool _multiTouch;
    private long _contactId = -1;
    private double _startX;
    private double _startY;
    private double _startOffset;

    /// <summary>
    /// Initializes a new instance of the <see cref="TouchPanSession"/> class.
    /// </summary>
    /// <param name="threshold">The vertical travel threshold in DIPs; hostile values use the default.</param>
    public TouchPanSession(double? threshold = null)
    {
        _threshold = threshold is double requested && double.IsFinite(requested) && requested > 0
            ? requested
            : DefaultThreshold;
    }

    /// <summary>Gets a value indicating whether this session is following one contact's tap/pan.</summary>
    public bool IsTracking => _tracking;

    /// <summary>Gets a value indicating whether the current contact has started panning.</summary>
    public bool IsPanning => _panning;

    /// <summary>Gets a value indicating whether more than one contact is (or was) down.</summary>
    public bool IsMultiTouch => _multiTouch;

    /// <summary>Gets the tracked contact's identifier, or -1 when idle.</summary>
    public long ContactId => _tracking ? _contactId : -1;

    /// <summary>
    /// Registers a contact going down. A second simultaneous contact cancels the gesture and blocks
    /// panning until all contacts have lifted; a non-finite position also leaves the session idle.
    /// </summary>
    /// <param name="contactId">The touch device identifier.</param>
    /// <param name="x">Contact X in DIPs, relative to the scroll surface.</param>
    /// <param name="y">Contact Y in DIPs, relative to the scroll surface.</param>
    /// <param name="startOffset">The scroll surface's vertical offset when the contact went down.</param>
    /// <returns><see langword="true"/> when this contact is now the tracked tap/pan contact.</returns>
    public bool Begin(long contactId, double x, double y, double startOffset)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(startOffset))
        {
            Cancel();
            return false;
        }

        if (!_contacts.Add(contactId))
        {
            // A repeated down for a contact already known: ignore it.
            return false;
        }

        if (_multiTouch || _contacts.Count > 1)
        {
            _multiTouch = true;
            ResetGesture();
            return false;
        }

        _contactId = contactId;
        _startX = x;
        _startY = y;
        _startOffset = startOffset;
        _tracking = true;
        _panning = false;
        return true;
    }

    /// <summary>
    /// Feeds one move for the tracked contact. Below the threshold (or while the horizontal travel
    /// dominates) the result is <see cref="TouchPanUpdate.None"/>; once the gesture is recognized as
    /// vertical it reports the clamped offset for every subsequent move until <see cref="End"/>.
    /// </summary>
    /// <param name="contactId">The touch device identifier.</param>
    /// <param name="x">Current X in DIPs.</param>
    /// <param name="y">Current Y in DIPs.</param>
    /// <param name="scrollableHeight">The scroll surface's vertically scrollable extent in DIPs.</param>
    /// <returns>The pan step; never <see langword="null"/>.</returns>
    public TouchPanUpdate Move(long contactId, double x, double y, double scrollableHeight)
    {
        if (!_tracking || _multiTouch || contactId != _contactId)
        {
            return TouchPanUpdate.None;
        }

        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            Cancel();
            return TouchPanUpdate.None;
        }

        var deltaX = x - _startX;
        var deltaY = y - _startY;

        if (!_panning)
        {
            if (Math.Abs(deltaY) <= _threshold || Math.Abs(deltaY) <= Math.Abs(deltaX))
            {
                return TouchPanUpdate.None;
            }

            _panning = true;
        }

        var maxOffset = double.IsFinite(scrollableHeight) && scrollableHeight > 0 ? scrollableHeight : 0d;
        var target = Math.Clamp(_startOffset - deltaY, 0d, maxOffset);
        return new TouchPanUpdate(true, target);
    }

    /// <summary>
    /// Ends a contact. Reports whether that contact was panning so the view can consume the matching
    /// release (and never turn a pan into a click). Panning stops for the ended contact; a new session
    /// can start once every contact has lifted.
    /// </summary>
    /// <param name="contactId">The touch device identifier.</param>
    /// <returns><see langword="true"/> when the ending contact had started panning.</returns>
    public bool End(long contactId)
    {
        var wasPanning = _panning && contactId == _contactId;
        _contacts.Remove(contactId);

        if (contactId == _contactId)
        {
            ResetGesture();
        }

        if (_contacts.Count == 0)
        {
            _multiTouch = false;
        }

        return wasPanning;
    }

    /// <summary>Abandons the current gesture (lost capture, hostile input) without scrolling.</summary>
    public void Cancel()
    {
        _contacts.Clear();
        _multiTouch = false;
        ResetGesture();
    }

    private void ResetGesture()
    {
        _tracking = false;
        _panning = false;
        _contactId = -1;
        _startX = 0d;
        _startY = 0d;
        _startOffset = 0d;
    }
}
