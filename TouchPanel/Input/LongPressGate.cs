namespace TouchPanel.Input;

/// <summary>
/// A pure, WPF-free press/hold state machine that decides when a press has been held long enough to
/// count as a long press (UX fix #1). The caller owns the timing source: it starts a timer when a
/// widget is pressed and feeds the current instant to <see cref="Elapsed"/>; the gate reports the
/// long press exactly once while the press is still held. A release before the threshold is a short
/// press, so the caller lets the normal tap behavior proceed.
/// </summary>
/// <remarks>
/// Keeping the decision logic here (instead of in the view) keeps it deterministic and unit-testable
/// with explicit instants and no real clock, while the touch/mouse specifics stay in the view. The
/// type is deliberately free of WPF and timing dependencies so <c>Input/</c> stays WPF-free.
/// </remarks>
public sealed class LongPressGate
{
    /// <summary>The default press-and-hold threshold (~500 ms).</summary>
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromMilliseconds(500);

    private DateTimeOffset? _pressedAt;
    private bool _longPressRaised;

    /// <summary>
    /// Initializes a new instance of the <see cref="LongPressGate"/> class.
    /// </summary>
    /// <param name="threshold">
    /// The hold duration that counts as a long press; non-positive values fall back to
    /// <see cref="DefaultThreshold"/>.
    /// </param>
    public LongPressGate(TimeSpan? threshold = null)
    {
        var value = threshold ?? DefaultThreshold;
        Threshold = value > TimeSpan.Zero ? value : DefaultThreshold;
    }

    /// <summary>Gets the hold duration that counts as a long press.</summary>
    public TimeSpan Threshold { get; }

    /// <summary>Gets a value indicating whether a press is currently in progress.</summary>
    public bool IsPressed => _pressedAt.HasValue;

    /// <summary>
    /// Gets a value indicating whether the current press already crossed the long-press threshold.
    /// A pending release uses this to decide whether to suppress the normal tap behavior.
    /// </summary>
    public bool LongPressRaised => _longPressRaised;

    /// <summary>
    /// Records the start of a press at <paramref name="now"/>, resetting any previous gesture.
    /// </summary>
    /// <param name="now">The instant the press began.</param>
    public void Press(DateTimeOffset now)
    {
        _pressedAt = now;
        _longPressRaised = false;
    }

    /// <summary>
    /// Reports whether the held press has now crossed the threshold. Returns <see langword="true"/>
    /// only on the first call that reaches it for the current press, so the caller acts once.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns><see langword="true"/> when the long press should fire.</returns>
    public bool Elapsed(DateTimeOffset now)
    {
        if (_pressedAt is not { } start || _longPressRaised)
        {
            return false;
        }

        if (now - start < Threshold)
        {
            return false;
        }

        _longPressRaised = true;
        return true;
    }

    /// <summary>
    /// Ends the current press. Returns <see langword="true"/> when no long press was raised for it,
    /// i.e. it was a short tap that should keep its normal behavior; <see langword="false"/> when a
    /// long press already fired (so the caller suppresses the tap) or when no press was tracked.
    /// </summary>
    /// <returns><see langword="true"/> when the released press was a short tap.</returns>
    public bool Release()
    {
        var wasShortPress = _pressedAt.HasValue && !_longPressRaised;
        _pressedAt = null;
        _longPressRaised = false;
        return wasShortPress;
    }

    /// <summary>Abandons the current gesture without counting it as a press or a tap.</summary>
    public void Cancel()
    {
        _pressedAt = null;
        _longPressRaised = false;
    }
}
