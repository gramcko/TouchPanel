using TouchPanel.Input;

namespace TouchPanel.Tests.Input;

/// <summary>
/// Long-press decision logic (UX fix #1): a press held at or beyond the ~500 ms threshold fires once
/// and suppresses the normal tap, while a shorter press is a short tap that keeps its behavior. The
/// gate is deterministic (explicit instants, no real clock), so this needs no UI or timing.
/// </summary>
public sealed class LongPressGateTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Elapsed_FiresOnceAtTheThresholdWhileHeld()
    {
        var gate = new LongPressGate();
        gate.Press(T0);

        Assert.True(gate.IsPressed);
        Assert.False(gate.LongPressRaised);

        Assert.False(gate.Elapsed(T0 + TimeSpan.FromMilliseconds(499)));
        Assert.True(gate.Elapsed(T0 + TimeSpan.FromMilliseconds(500)));
        Assert.True(gate.LongPressRaised);

        // Only fires once per press.
        Assert.False(gate.Elapsed(T0 + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Release_BeforeThreshold_IsAShortPress()
    {
        var gate = new LongPressGate();
        gate.Press(T0);

        Assert.True(gate.Release());
        Assert.False(gate.IsPressed);
        Assert.False(gate.LongPressRaised);

        // A release with no tracked press is neither short nor long.
        Assert.False(gate.Release());
        Assert.False(gate.Elapsed(T0 + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Release_AfterLongPress_IsNotAShortPress()
    {
        var gate = new LongPressGate();
        gate.Press(T0);
        Assert.True(gate.Elapsed(T0 + TimeSpan.FromSeconds(1)));

        Assert.False(gate.Release());
        Assert.False(gate.IsPressed);
        Assert.False(gate.LongPressRaised);
    }

    [Fact]
    public void CustomThreshold_IsHonoured()
    {
        var gate = new LongPressGate(TimeSpan.FromMilliseconds(200));
        Assert.Equal(TimeSpan.FromMilliseconds(200), gate.Threshold);

        gate.Press(T0);
        Assert.False(gate.Elapsed(T0 + TimeSpan.FromMilliseconds(199)));
        Assert.True(gate.Elapsed(T0 + TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void Cancel_AbandonsTheGesture()
    {
        var gate = new LongPressGate();
        gate.Press(T0);
        gate.Cancel();

        Assert.False(gate.IsPressed);
        Assert.False(gate.Elapsed(T0 + TimeSpan.FromSeconds(1)));
        Assert.False(gate.Release());
    }
}
