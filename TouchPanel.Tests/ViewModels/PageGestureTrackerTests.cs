using TouchPanel.ViewModels.Paging;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Pure horizontal paging gestures (user-requested phone-homepage pager): the threshold and
/// direction policy, vertical/diagonal rejection, and the structural guarantee that a recognized
/// swipe consumes the release so the widget under the finger cannot execute its tap action.
/// </summary>
public sealed class PageGestureTrackerTests
{
    [Theory]
    [InlineData(-200, 20, PageSwipeDirection.Next)]
    [InlineData(200, 20, PageSwipeDirection.Previous)]
    [InlineData(-72, 0, PageSwipeDirection.Next)]
    [InlineData(72, 0, PageSwipeDirection.Previous)]
    public void TryResolveSwipe_RecognizesHorizontalTravelAndDirection(
        double deltaX,
        double deltaY,
        PageSwipeDirection expected)
    {
        var recognized = PageGestureTracker.TryResolveSwipe(
            deltaX,
            deltaY,
            PageGestureTracker.DefaultThreshold,
            PageGestureTracker.DefaultDirectionRatio,
            out var direction);

        Assert.True(recognized);
        Assert.Equal(expected, direction);
    }

    [Theory]
    [InlineData(-71.9, 0)]
    [InlineData(0, 200)]
    [InlineData(-100, 80)]
    [InlineData(100, -80)]
    public void TryResolveSwipe_RejectsShortVerticalAndDiagonalTravel(double deltaX, double deltaY)
    {
        var recognized = PageGestureTracker.TryResolveSwipe(
            deltaX,
            deltaY,
            PageGestureTracker.DefaultThreshold,
            PageGestureTracker.DefaultDirectionRatio,
            out var direction);

        Assert.False(recognized);
        Assert.Equal(PageSwipeDirection.None, direction);
    }

    [Fact]
    public void SwipeLeft_ReportsSuppressedMovesAndExactlyOneNextNavigation()
    {
        var tracker = new PageGestureTracker();

        Assert.True(tracker.Begin(500, 300));
        Assert.Equal(PageGestureOutcome.Ignore, tracker.Move(460, 300));

        var move = tracker.Move(300, 300);
        Assert.True(move.Handled);
        Assert.Equal(PageSwipeDirection.None, move.Navigation);
        Assert.True(tracker.IsSwipeRecognized);

        // Further moves stay suppressed but never report another navigation.
        Assert.True(tracker.Move(200, 300).Handled);

        var complete = tracker.Complete(180, 300);
        Assert.True(complete.Handled);
        Assert.Equal(PageSwipeDirection.Next, complete.Navigation);
        Assert.False(tracker.IsTracking);

        // A release after the gesture is a normal event for the widgets.
        Assert.Equal(PageGestureOutcome.Ignore, tracker.Complete(180, 300));
    }

    [Fact]
    public void RecognizedDirection_IsKept_EvenWhenTheFingerReturnsTowardTheStart()
    {
        var tracker = new PageGestureTracker();
        tracker.Begin(400, 200);
        Assert.True(tracker.Move(250, 200).Handled);

        // The direction is latched at recognition; releasing near the start must not turn the swipe
        // into the opposite page.
        var complete = tracker.Complete(390, 200);

        Assert.True(complete.Handled);
        Assert.Equal(PageSwipeDirection.Next, complete.Navigation);
    }

    [Fact]
    public void SwipeRight_ReportsPrevious()
    {
        var tracker = new PageGestureTracker();
        tracker.Begin(100, 200);
        Assert.True(tracker.Move(300, 200).Handled);

        var complete = tracker.Complete(320, 200);

        Assert.True(complete.Handled);
        Assert.Equal(PageSwipeDirection.Previous, complete.Navigation);
    }

    [Fact]
    public void VerticalDrag_IsIgnoredAndAbandoned()
    {
        var tracker = new PageGestureTracker();
        tracker.Begin(100, 100);

        var move = tracker.Move(110, 260);

        Assert.False(move.Handled);
        Assert.False(tracker.IsTracking);
        Assert.Equal(PageGestureOutcome.Ignore, tracker.Complete(110, 300));
    }

    [Fact]
    public void Tap_DoesNotSuppressOrNavigate()
    {
        var tracker = new PageGestureTracker();
        tracker.Begin(50, 50);

        Assert.Equal(PageGestureOutcome.Ignore, tracker.Move(54, 53));
        Assert.Equal(PageGestureOutcome.Ignore, tracker.Complete(54, 53));
    }

    [Fact]
    public void RecognizedSwipe_HandlesTheRelease_SoTheWidgetTapCannotFire()
    {
        var tracker = new PageGestureTracker();
        tracker.Begin(400, 200);
        tracker.Move(250, 200);

        var release = tracker.Complete(250, 200);

        // The view marks the release handled when Handled is true, so the widget's Button never sees
        // its matching mouse-up and its action is not executed for a page swipe.
        Assert.True(release.Handled);
        Assert.Equal(PageSwipeDirection.Next, release.Navigation);
    }

    [Fact]
    public void Cancel_AbandonsTheGestureWithoutNavigationOrSuppression()
    {
        var tracker = new PageGestureTracker();
        tracker.Begin(400, 200);
        tracker.Move(200, 200);

        tracker.Cancel();

        Assert.False(tracker.IsTracking);
        Assert.Equal(PageGestureOutcome.Ignore, tracker.Complete(200, 200));
    }

    [Fact]
    public void HostilePositionsAndOptions_NeverArmANavigation()
    {
        var tracker = new PageGestureTracker(double.NaN, double.PositiveInfinity);

        Assert.False(tracker.Begin(double.NaN, 0));
        Assert.Equal(PageGestureOutcome.Ignore, tracker.Move(0, 0));

        Assert.True(tracker.Begin(0, 0));
        Assert.Equal(PageGestureOutcome.Ignore, tracker.Move(double.NaN, 0));
        Assert.Equal(PageGestureOutcome.Ignore, tracker.Complete(double.NaN, 0));
    }
}
