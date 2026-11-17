using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Paging;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Page-transition policy coverage (user-requested addition): each mode produces the documented
/// duration, easing, and start values; the direction-aware slide rotation maps swipes correctly;
/// <c>none</c>, edit mode, the initial startup render, and a page without a measured size skip the
/// animation, while the OS animation-effects preference (<c>reducedMotion</c>) never suppresses an
/// explicit animated mode. Also covers the direction the pager threads into the page switch
/// (next/left = forward, previous/right = backward, tab/Open = forward).
/// </summary>
public sealed class PageTransitionPlannerTests
{
    private const double Width = 800;
    private const double Height = 600;

    [Fact]
    public void Slide_Forward_EntersFromTheRightAndPushesTheOldPageLeft()
    {
        var plan = PageTransitionPlanner.Create(
            PageTransitionKind.Slide,
            PageTransitionDirection.Forward,
            Width,
            Height);

        Assert.True(plan.Animate);
        Assert.Equal(PageTransitionKind.Slide, plan.Kind);
        Assert.Equal(PageTransitionPlanner.SlideDurationMilliseconds, plan.DurationMilliseconds);
        Assert.Equal(PageTransitionEasing.EaseOutCubic, plan.Easing);
        Assert.Equal(Width, plan.IncomingOffsetX);
        Assert.Equal(0, plan.IncomingOffsetY);
        Assert.Equal(1, plan.IncomingStartOpacity);
        Assert.Equal(1, plan.IncomingStartScale);
        Assert.Equal(-Width, plan.OutgoingOffsetX);
        Assert.Equal(1, plan.OutgoingEndOpacity);
    }

    [Fact]
    public void Slide_Backward_EntersFromTheLeft()
    {
        var plan = PageTransitionPlanner.Create(
            PageTransitionKind.Slide,
            PageTransitionDirection.Backward,
            Width,
            Height);

        Assert.Equal(-Width, plan.IncomingOffsetX);
        Assert.Equal(Width, plan.OutgoingOffsetX);
    }

    [Fact]
    public void Fade_CrossfadesWithoutMovement()
    {
        var plan = PageTransitionPlanner.Create(PageTransitionKind.Fade, PageTransitionDirection.Forward, Width, Height);

        Assert.True(plan.Animate);
        Assert.Equal(PageTransitionPlanner.FadeDurationMilliseconds, plan.DurationMilliseconds);
        Assert.Equal(PageTransitionEasing.EaseOutCubic, plan.Easing);
        Assert.Equal(0, plan.IncomingOffsetX);
        Assert.Equal(0, plan.IncomingOffsetY);
        Assert.Equal(PageTransitionPlanner.FadeStartOpacity, plan.IncomingStartOpacity);
        Assert.Equal(1, plan.IncomingStartScale);
        Assert.Equal(0, plan.OutgoingEndOpacity);
    }

    [Fact]
    public void SlideUp_RisesFromTheBottomAndPushesTheOldPageOutOfTheTop()
    {
        var plan = PageTransitionPlanner.Create(PageTransitionKind.SlideUp, PageTransitionDirection.Forward, Width, Height);

        Assert.True(plan.Animate);
        Assert.Equal(PageTransitionPlanner.SlideUpDurationMilliseconds, plan.DurationMilliseconds);
        Assert.Equal(0, plan.IncomingOffsetX);
        Assert.Equal(Height, plan.IncomingOffsetY);
        Assert.Equal(1, plan.IncomingStartOpacity);
        Assert.Equal(0, plan.OutgoingOffsetX);

        // The old-page snapshot rises out of view too; holding it still covered the incoming page
        // for the whole duration and hid the animation.
        Assert.Equal(-Height, plan.OutgoingOffsetY);
        Assert.Equal(1, plan.OutgoingEndOpacity);
    }

    [Fact]
    public void Zoom_SettlesWithSubtleScaleAndOpacity()
    {
        var plan = PageTransitionPlanner.Create(PageTransitionKind.Zoom, PageTransitionDirection.Forward, Width, Height);

        Assert.True(plan.Animate);
        Assert.Equal(PageTransitionPlanner.ZoomDurationMilliseconds, plan.DurationMilliseconds);
        Assert.Equal(PageTransitionPlanner.ZoomStartScale, plan.IncomingStartScale);
        Assert.Equal(PageTransitionPlanner.ZoomStartOpacity, plan.IncomingStartOpacity);
        Assert.Equal(0, plan.IncomingOffsetX);
        Assert.Equal(0, plan.OutgoingEndOpacity);
    }

    [Theory]
    [InlineData(PageTransitionKind.Slide)]
    [InlineData(PageTransitionKind.Fade)]
    [InlineData(PageTransitionKind.SlideUp)]
    [InlineData(PageTransitionKind.Zoom)]
    public void EveryAnimatedMode_StaysWithinTheRequestedDurationRange(PageTransitionKind kind)
    {
        var plan = PageTransitionPlanner.Create(kind, PageTransitionDirection.Forward, Width, Height);

        Assert.InRange(plan.DurationMilliseconds, 200, 300);
        Assert.Equal(PageTransitionEasing.EaseOutCubic, plan.Easing);
    }

    [Fact]
    public void None_NeverAnimates()
    {
        var plan = PageTransitionPlanner.Create(PageTransitionKind.None, PageTransitionDirection.Forward, Width, Height);

        Assert.False(plan.Animate);
        Assert.Equal(0, plan.DurationMilliseconds);
    }

    [Theory]
    [InlineData(PageTransitionKind.Slide, 240d, 800d, 0d, 1d, 1d)]
    [InlineData(PageTransitionKind.Fade, 200d, 0d, 0d, PageTransitionPlanner.FadeStartOpacity, 1d)]
    [InlineData(PageTransitionKind.SlideUp, 260d, 0d, 600d, 1d, 1d)]
    [InlineData(PageTransitionKind.Zoom, 220d, 0d, 0d, PageTransitionPlanner.ZoomStartOpacity, PageTransitionPlanner.ZoomStartScale)]
    public void ReducedMotion_KeepsTheExplicitAnimatedMode(
        PageTransitionKind kind,
        double duration,
        double offsetX,
        double offsetY,
        double startOpacity,
        double startScale)
    {
        // The user's explicit selection always wins: the OS animation-effects preference must not
        // turn an animated mode into an instant change.
        var plan = PageTransitionPlanner.Create(
            kind,
            PageTransitionDirection.Forward,
            Width,
            Height,
            reducedMotion: true);

        Assert.True(plan.Animate);
        Assert.Equal(kind, plan.Kind);
        Assert.Equal(duration, plan.DurationMilliseconds);
        Assert.Equal(PageTransitionEasing.EaseOutCubic, plan.Easing);
        Assert.Equal(offsetX, plan.IncomingOffsetX);
        Assert.Equal(offsetY, plan.IncomingOffsetY);
        Assert.Equal(startOpacity, plan.IncomingStartOpacity);
        Assert.Equal(startScale, plan.IncomingStartScale);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EditModeAndStartup_SkipTheAnimation(bool editMode, bool initial)
    {
        var plan = PageTransitionPlanner.Create(
            PageTransitionKind.Slide,
            PageTransitionDirection.Forward,
            Width,
            Height,
            reducedMotion: false,
            editMode,
            initial);

        Assert.False(plan.Animate);
        Assert.Equal(0, plan.DurationMilliseconds);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, 0)]
    [InlineData(-10, 600)]
    [InlineData(double.NaN, 600)]
    [InlineData(double.PositiveInfinity, 600)]
    public void UnmeasuredPage_SkipsTheAnimation(double width, double height)
    {
        var plan = PageTransitionPlanner.Create(PageTransitionKind.Slide, PageTransitionDirection.Forward, width, height);

        Assert.False(plan.Animate);
    }

    [Theory]
    [InlineData(PageSwipeDirection.Next, PageTransitionDirection.Forward)]
    [InlineData(PageSwipeDirection.Previous, PageTransitionDirection.Backward)]
    [InlineData(PageSwipeDirection.None, PageTransitionDirection.Forward)]
    public void SwipeDirection_MapsToTheSlideDirection(PageSwipeDirection swipe, PageTransitionDirection expected)
    {
        Assert.Equal(expected, PageTransitionPlanner.DirectionForSwipe(swipe));
    }

    [Fact]
    public async Task Pager_NextAndPrevious_ThreadForwardAndBackward()
    {
        var service = new RecordingSwitchService();
        var config = new PanelConfig
        {
            ActivePageId = "a",
            Pages = [new PageConfig { Id = "a" }, new PageConfig { Id = "b" }, new PageConfig { Id = "c" }],
        };
        var viewModel = new PageSwitcherViewModel(config, service);

        await viewModel.MoveByAsync(1);
        await viewModel.MoveByAsync(-1);

        Assert.Equal(
            [("b", PageTransitionDirection.Forward), ("a", PageTransitionDirection.Backward)],
            service.Requests);
    }

    [Fact]
    public async Task TabSelection_DefaultsToForward()
    {
        var service = new RecordingSwitchService();
        var config = new PanelConfig
        {
            ActivePageId = "a",
            Pages = [new PageConfig { Id = "a" }, new PageConfig { Id = "b" }],
        };
        var viewModel = new PageSwitcherViewModel(config, service);

        await viewModel.SwitchToAsync("b");

        Assert.Equal(("b", PageTransitionDirection.Forward), Assert.Single(service.Requests));
    }

    private sealed class RecordingSwitchService : IPageSwitchService
    {
        public List<(string PageId, PageTransitionDirection Direction)> Requests { get; } = [];

        public Task<PageSwitchResult> SwitchAsync(string requestedPageId, CancellationToken cancellationToken = default) =>
            SwitchAsync(requestedPageId, PageTransitionDirection.Forward, cancellationToken);

        public Task<PageSwitchResult> SwitchAsync(
            string requestedPageId,
            PageTransitionDirection direction,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((requestedPageId, direction));
            return Task.FromResult(new PageSwitchResult { Changed = true, ResolvedPageId = requestedPageId });
        }
    }
}
