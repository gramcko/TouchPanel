using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Pure touch drag-to-scroll policy (user-reported fix: a vertical drag that starts on a field must
/// scroll the panel). The session is WPF-free, so these tests pin the threshold, direction, clamping,
/// multi-touch, and lifecycle rules without a window or touch hardware.
/// </summary>
public sealed class TouchPanSessionTests
{
    private const long Contact = 7;
    private const double Scrollable = 500;

    [Fact]
    public void Move_BelowTheThreshold_ReportsNoPan()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 0));

        var update = session.Move(Contact, 104, 207, Scrollable);

        Assert.False(update.IsPanning);
        Assert.Equal(TouchPanUpdate.None, update);
        Assert.True(session.IsTracking);
        Assert.False(session.IsPanning);
    }

    [Fact]
    public void Move_VerticalDominantPastTheThreshold_StartsAndComputesTheTarget()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 0));

        var update = session.Move(Contact, 101, 180, Scrollable);

        // Drag up by 20 DIP: the content follows the finger, so the offset grows by 20.
        Assert.True(update.IsPanning);
        Assert.Equal(20, update.Offset);
        Assert.True(session.IsPanning);
    }

    [Fact]
    public void Move_Overscroll_ClampsAtBothEnds()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 100));

        var bottom = session.Move(Contact, 100, -1000, Scrollable);
        Assert.True(bottom.IsPanning);
        Assert.Equal(Scrollable, bottom.Offset);

        var top = session.Move(Contact, 100, 2000, Scrollable);
        Assert.True(top.IsPanning);
        Assert.Equal(0, top.Offset);
    }

    [Fact]
    public void Move_HorizontalDominant_StaysOutOfTheGesture()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 0));

        var update = session.Move(Contact, 160, 209, Scrollable);

        Assert.False(update.IsPanning);
        Assert.False(session.IsPanning);
    }

    [Fact]
    public void Move_HorizontalDominantAtThreshold_StaysOut()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 0));

        // Exactly 8 DIP vertical is not past the strict "greater than threshold" rule, and is not
        // strictly larger than the horizontal travel.
        Assert.False(session.Move(Contact, 100, 208, Scrollable).IsPanning);
        Assert.False(session.Move(Contact, 108, 208, Scrollable).IsPanning);

        // A clearly vertical move afterwards still starts the pan (upwards, so the offset grows).
        var started = session.Move(Contact, 100, 170, Scrollable);
        Assert.True(started.IsPanning);
        Assert.Equal(30, started.Offset);
    }

    [Fact]
    public void Begin_SecondContact_CancelsAndBlocksUntilEveryContactLifts()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 0));
        Assert.True(session.Move(Contact, 100, 150, Scrollable).IsPanning);

        Assert.False(session.Begin(Contact + 1, 300, 300, 0));
        Assert.True(session.IsMultiTouch);
        Assert.False(session.IsTracking);
        Assert.False(session.Move(Contact, 100, 100, Scrollable).IsPanning);

        // Lifting the second contact alone does not rearm the gesture.
        Assert.False(session.End(Contact + 1));
        Assert.True(session.IsMultiTouch);
        Assert.False(session.Move(Contact, 100, 50, Scrollable).IsPanning);

        // Once every contact is up, a fresh contact starts a new session.
        session.End(Contact);
        Assert.False(session.IsMultiTouch);
        Assert.True(session.Begin(Contact + 2, 100, 200, 0));
        Assert.True(session.Move(Contact + 2, 100, 180, Scrollable).IsPanning);
    }

    [Fact]
    public void End_ResetsTheGestureAndReportsWhetherItWasPanning()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 0));
        Assert.True(session.Move(Contact, 100, 150, Scrollable).IsPanning);

        Assert.True(session.End(Contact));
        Assert.False(session.IsTracking);
        Assert.False(session.IsPanning);

        // Moves from the lifted contact are ignored.
        Assert.False(session.Move(Contact, 100, 0, Scrollable).IsPanning);

        // A new session after the end works.
        Assert.True(session.Begin(Contact, 100, 200, 10));
        var update = session.Move(Contact, 100, 160, Scrollable);
        Assert.True(update.IsPanning);
        Assert.Equal(50, update.Offset);
    }

    [Fact]
    public void Cancel_LostCapture_ResetsEverything()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 0));
        Assert.True(session.Move(Contact, 100, 150, Scrollable).IsPanning);

        session.Cancel();

        Assert.False(session.IsTracking);
        Assert.False(session.IsPanning);
        Assert.False(session.Move(Contact, 100, 0, Scrollable).IsPanning);
        Assert.True(session.Begin(Contact, 100, 200, 0));
    }

    [Fact]
    public void Begin_RejectsNonFiniteInputAndIgnoresOtherContacts()
    {
        var session = new TouchPanSession();

        Assert.False(session.Begin(Contact, double.NaN, 200, 0));
        Assert.False(session.IsTracking);

        Assert.True(session.Begin(Contact, 100, 200, 0));
        Assert.False(session.Begin(Contact, 100, 200, 0));
        Assert.True(session.IsTracking);

        // A move from a contact the session never saw cannot pan.
        Assert.False(session.Move(Contact + 9, 100, 0, Scrollable).IsPanning);
    }

    [Fact]
    public void Move_WithoutScrollableExtent_ClampsToZero()
    {
        var session = new TouchPanSession();
        Assert.True(session.Begin(Contact, 100, 200, 0));

        var update = session.Move(Contact, 100, 100, 0);

        Assert.True(update.IsPanning);
        Assert.Equal(0, update.Offset);
    }

    [Fact]
    public void Threshold_IsConfigurableAndHostileValuesUseTheDefault()
    {
        var custom = new TouchPanSession(threshold: 20);
        Assert.True(custom.Begin(Contact, 100, 200, 0));
        Assert.False(custom.Move(Contact, 100, 188, Scrollable).IsPanning);
        Assert.True(custom.Move(Contact, 100, 178, Scrollable).IsPanning);

        var hostile = new TouchPanSession(threshold: double.NaN);
        Assert.True(hostile.Begin(Contact, 100, 200, 0));
        Assert.False(hostile.Move(Contact, 100, 195, Scrollable).IsPanning);
        Assert.True(hostile.Move(Contact, 100, 190, Scrollable).IsPanning);
    }
}
