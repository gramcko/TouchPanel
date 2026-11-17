using TouchPanel.Input;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Empty-space long-press behavior (user-requested): a stationary hold on the page background
/// requests edit mode with the same ~500 ms threshold as a widget hold, while a tap or a horizontal
/// page swipe never does. No timer, window, or device input is involved.
/// </summary>
public sealed class EmptySpaceLongPressTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Hold_EntersEditModeOnce_AndSuppressesTheRelease()
    {
        var entered = 0;
        var coordinator = new EmptySpaceLongPressCoordinator(() => entered++);

        coordinator.Press(T0);
        Assert.True(coordinator.IsPressed);

        Assert.True(coordinator.Elapsed(T0 + LongPressGate.DefaultThreshold));
        Assert.Equal(1, entered);

        // A later tick for the same gesture must not request edit mode again.
        Assert.False(coordinator.Elapsed(T0 + LongPressGate.DefaultThreshold + TimeSpan.FromSeconds(1)));
        Assert.Equal(1, entered);

        Assert.True(coordinator.Release());
        Assert.False(coordinator.IsPressed);
    }

    [Fact]
    public void ShortTap_DoesNotEnterEditMode_AndDoesNotSuppressTheTap()
    {
        var entered = 0;
        var coordinator = new EmptySpaceLongPressCoordinator(() => entered++);

        coordinator.Press(T0);
        Assert.False(coordinator.Elapsed(T0 + TimeSpan.FromMilliseconds(120)));
        Assert.False(coordinator.Release());

        Assert.Equal(0, entered);
    }

    [Fact]
    public void HorizontalSwipe_CancelsThePendingHold_SoNoEditModeEntryFires()
    {
        var entered = 0;
        var coordinator = new EmptySpaceLongPressCoordinator(() => entered++);

        coordinator.Press(T0);
        Assert.True(coordinator.Move(-24, 2));
        Assert.False(coordinator.IsPressed);
        Assert.False(coordinator.Elapsed(T0 + LongPressGate.DefaultThreshold));
        Assert.False(coordinator.Release());
        Assert.Equal(0, entered);
    }

    [Fact]
    public void VerticalMovement_KeepsTheHoldAlive()
    {
        var entered = 0;
        var coordinator = new EmptySpaceLongPressCoordinator(() => entered++);

        coordinator.Press(T0);
        Assert.False(coordinator.Move(2, 30));
        Assert.True(coordinator.Elapsed(T0 + LongPressGate.DefaultThreshold));
        Assert.Equal(1, entered);
    }

    [Fact]
    public void Cancel_AbandonsTheGestureWithoutASuppressedTap()
    {
        var entered = 0;
        var coordinator = new EmptySpaceLongPressCoordinator(() => entered++);

        coordinator.Press(T0);
        coordinator.Cancel();

        Assert.False(coordinator.IsPressed);
        Assert.False(coordinator.Elapsed(T0 + LongPressGate.DefaultThreshold));
        Assert.False(coordinator.Release());
        Assert.Equal(0, entered);
    }

    [Fact]
    public void Threshold_MatchesTheWidgetLongPressThreshold()
    {
        var coordinator = new EmptySpaceLongPressCoordinator(() => { });

        Assert.Equal(LongPressGate.DefaultThreshold, coordinator.Threshold);
    }
}
