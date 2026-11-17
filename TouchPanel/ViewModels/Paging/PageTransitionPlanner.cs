using TouchPanel.Config;

namespace TouchPanel.ViewModels.Paging;

/// <summary>The easing curve a page-transition animation uses.</summary>
public enum PageTransitionEasing
{
    /// <summary>Constant speed (not used by the current modes; available for tests/future modes).</summary>
    Linear = 0,

    /// <summary>Fast start, gentle settle (the default for every animated mode).</summary>
    EaseOutCubic,
}

/// <summary>
/// Pure description of one page transition (user-requested addition): whether it animates, the
/// duration/easing, and the start/end values of the incoming page and the outgoing snapshot. The WPF
/// controller turns this into WPF animations; keeping the parameters here makes the mode,
/// direction, edit-mode, and startup policy unit-testable without WPF or timing.
/// </summary>
/// <param name="Animate">Whether an animation should run at all.</param>
/// <param name="Kind">The configured transition kind.</param>
/// <param name="Direction">The page-change direction (forward/backward).</param>
/// <param name="DurationMilliseconds">The animation duration in milliseconds; <c>0</c> when not animating.</param>
/// <param name="Easing">The easing curve.</param>
/// <param name="IncomingOffsetX">Horizontal start offset of the incoming page, in DIPs.</param>
/// <param name="IncomingOffsetY">Vertical start offset of the incoming page, in DIPs.</param>
/// <param name="IncomingStartOpacity">Start opacity of the incoming page.</param>
/// <param name="IncomingStartScale">Start scale of the incoming page (1 = full size).</param>
/// <param name="OutgoingOffsetX">Horizontal end offset of the outgoing snapshot, in DIPs.</param>
/// <param name="OutgoingOffsetY">Vertical end offset of the outgoing snapshot, in DIPs.</param>
/// <param name="OutgoingEndOpacity">End opacity of the outgoing snapshot.</param>
public readonly record struct PageTransitionPlan(
    bool Animate,
    PageTransitionKind Kind,
    PageTransitionDirection Direction,
    double DurationMilliseconds,
    PageTransitionEasing Easing,
    double IncomingOffsetX,
    double IncomingOffsetY,
    double IncomingStartOpacity,
    double IncomingStartScale,
    double OutgoingOffsetX,
    double OutgoingOffsetY,
    double OutgoingEndOpacity)
{
    /// <summary>Gets the plan used when no animation should run (<c>none</c>, edit mode, startup, unmeasured page).</summary>
    public static PageTransitionPlan Instant { get; } = new(
        Animate: false,
        Kind: PageTransitionKind.None,
        Direction: PageTransitionDirection.Forward,
        DurationMilliseconds: 0,
        Easing: PageTransitionEasing.Linear,
        IncomingOffsetX: 0,
        IncomingOffsetY: 0,
        IncomingStartOpacity: 1,
        IncomingStartScale: 1,
        OutgoingOffsetX: 0,
        OutgoingOffsetY: 0,
        OutgoingEndOpacity: 1);
}

/// <summary>
/// WPF-free page-transition policy (user-requested addition). It maps the configured mode and the
/// page-change direction to concrete animation parameters and skips the animation when the mode is
/// <c>none</c>, the panel is in edit mode (page changes there already save/exit/re-enter), the page
/// has no measured size, or the call is the initial startup render. Nothing here touches WPF, so the
/// rules are deterministic and testable.
/// </summary>
/// <remarks>
/// The mode is chosen deliberately by the user, so an explicit selection always wins:
/// <c>none</c> is the only per-mode opt-out, and the OS animation-effects preference
/// (<c>SystemParameters.ClientAreaAnimation</c>, surfaced as <c>reducedMotion</c>) never suppresses
/// an animated mode.
/// </remarks>
public static class PageTransitionPlanner
{
    /// <summary>Duration of the horizontal slide, in milliseconds.</summary>
    public const double SlideDurationMilliseconds = 240;

    /// <summary>Duration of the crossfade, in milliseconds.</summary>
    public const double FadeDurationMilliseconds = 200;

    /// <summary>Duration of the vertical rise, in milliseconds.</summary>
    public const double SlideUpDurationMilliseconds = 260;

    /// <summary>Duration of the zoom settle, in milliseconds.</summary>
    public const double ZoomDurationMilliseconds = 220;

    /// <summary>Start scale of the zoom transition (a subtle 4% settle).</summary>
    public const double ZoomStartScale = 0.96;

    /// <summary>Start opacity of the zoom transition.</summary>
    public const double ZoomStartOpacity = 0.35;

    /// <summary>Start opacity of the crossfade.</summary>
    public const double FadeStartOpacity = 0;

    /// <summary>
    /// Maps a completed swipe to the transition direction: swiping left (<c>Next</c>) is forward, so
    /// the new page enters from the right; swiping right (<c>Previous</c>) is backward. A gesture
    /// without a page change is forward, which is also the tab/Open default.
    /// </summary>
    /// <param name="swipe">The resolved swipe direction.</param>
    /// <returns>The transition direction.</returns>
    public static PageTransitionDirection DirectionForSwipe(PageSwipeDirection swipe) =>
        swipe == PageSwipeDirection.Previous
            ? PageTransitionDirection.Backward
            : PageTransitionDirection.Forward;

    /// <summary>
    /// Determines whether a page surface has a usable measured size for a transition.
    /// </summary>
    /// <param name="width">The measured width in DIPs.</param>
    /// <param name="height">The measured height in DIPs.</param>
    /// <returns><see langword="true"/> when both dimensions are finite and positive.</returns>
    internal static bool HasMeasuredSize(double width, double height) =>
        double.IsFinite(width) && width > 0 && double.IsFinite(height) && height > 0;

    /// <summary>
    /// Creates the plan for one page change.
    /// </summary>
    /// <param name="kind">The configured transition kind.</param>
    /// <param name="direction">The page-change direction; tabs and Open pass <see cref="PageTransitionDirection.Forward"/>.</param>
    /// <param name="width">The measured page width in DIPs.</param>
    /// <param name="height">The measured page height in DIPs.</param>
    /// <param name="reducedMotion">The OS animation-effects preference. Retained for callers, but it
    /// no longer suppresses the animation: the mode is chosen deliberately by the user.</param>
    /// <param name="editMode">Whether an edit session owns the panel (animation disabled).</param>
    /// <param name="initial">Whether this is the initial startup render (animation disabled).</param>
    /// <returns>The plan; never <see langword="null"/> for a value type.</returns>
    public static PageTransitionPlan Create(
        PageTransitionKind kind,
        PageTransitionDirection direction,
        double width,
        double height,
        bool reducedMotion = false,
        bool editMode = false,
        bool initial = false)
    {
        // The OS animation-effects preference no longer overrides an explicit user selection; only
        // the per-mode opt-out (none), edit mode, the startup render, and an unmeasured page skip.
        _ = reducedMotion;

        if (kind == PageTransitionKind.None || editMode || initial || !HasMeasuredSize(width, height))
        {
            return PageTransitionPlan.Instant with { Kind = kind, Direction = direction };
        }

        return kind switch
        {
            PageTransitionKind.Fade => new PageTransitionPlan(
                Animate: true,
                Kind: kind,
                Direction: direction,
                DurationMilliseconds: FadeDurationMilliseconds,
                Easing: PageTransitionEasing.EaseOutCubic,
                IncomingOffsetX: 0,
                IncomingOffsetY: 0,
                IncomingStartOpacity: FadeStartOpacity,
                IncomingStartScale: 1,
                OutgoingOffsetX: 0,
                OutgoingOffsetY: 0,
                OutgoingEndOpacity: 0),

            // A paired vertical slide: the new page rises from the bottom while the (opaque) old-page
            // snapshot rises out of the top. Leaving the snapshot in place would cover the incoming
            // page for the whole duration and hide the motion entirely.
            PageTransitionKind.SlideUp => new PageTransitionPlan(
                Animate: true,
                Kind: kind,
                Direction: direction,
                DurationMilliseconds: SlideUpDurationMilliseconds,
                Easing: PageTransitionEasing.EaseOutCubic,
                IncomingOffsetX: 0,
                IncomingOffsetY: height,
                IncomingStartOpacity: 1,
                IncomingStartScale: 1,
                OutgoingOffsetX: 0,
                OutgoingOffsetY: -height,
                OutgoingEndOpacity: 1),

            PageTransitionKind.Zoom => new PageTransitionPlan(
                Animate: true,
                Kind: kind,
                Direction: direction,
                DurationMilliseconds: ZoomDurationMilliseconds,
                Easing: PageTransitionEasing.EaseOutCubic,
                IncomingOffsetX: 0,
                IncomingOffsetY: 0,
                IncomingStartOpacity: ZoomStartOpacity,
                IncomingStartScale: ZoomStartScale,
                OutgoingOffsetX: 0,
                OutgoingOffsetY: 0,
                OutgoingEndOpacity: 0),

            _ => new PageTransitionPlan(
                Animate: true,
                Kind: PageTransitionKind.Slide,
                Direction: direction,
                DurationMilliseconds: SlideDurationMilliseconds,
                Easing: PageTransitionEasing.EaseOutCubic,
                IncomingOffsetX: direction == PageTransitionDirection.Backward ? -width : width,
                IncomingOffsetY: 0,
                IncomingStartOpacity: 1,
                IncomingStartScale: 1,
                OutgoingOffsetX: direction == PageTransitionDirection.Backward ? width : -width,
                OutgoingOffsetY: 0,
                OutgoingEndOpacity: 1),
        };
    }
}
