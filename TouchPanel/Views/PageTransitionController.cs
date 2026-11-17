using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using TouchPanel.Config;
using TouchPanel.ViewModels.Paging;

namespace TouchPanel.Views;

/// <summary>
/// Plays the normal-mode page-change animation (user-requested addition): captures the outgoing page
/// into a frozen <see cref="RenderTargetBitmap"/> overlay, lets the caller swap the page content, and
/// animates the incoming page plus the outgoing snapshot (no sleeps and no UI thread blocking).
/// </summary>
/// <remarks>
/// <para>
/// <b>How the motion is applied.</b> Every value comes from an <see cref="AnimationTimeline"/> applied
/// straight to its target with <see cref="Animatable.BeginAnimation(DependencyProperty, AnimationTimeline, HandoffBehavior)"/>
/// (and the same call on <see cref="UIElement"/> for opacity). A <c>Storyboard</c> cannot be used for
/// this: WPF silently ignores a storyboard animation whose explicit target is a <see cref="Freezable"/>
/// that is not part of the storyboard's containing object, so a transform reachable only through the
/// page host's <c>RenderTransform</c> (and the sibling overlay image) would never move. Applying the
/// animation to the <see cref="Animatable"/> itself needs no property path and works for both the
/// incoming page's transforms and the outgoing overlay, so the values actually progress.
/// </para>
/// <para>
/// <b>Overlay lifecycle.</b> The snapshot lives in a dedicated, hit-test-invisible <see cref="Grid"/>
/// above the page host and below the selector, so the incoming page is interactive immediately and
/// no widget input is intercepted. The overlay is removed and its bitmap released when the last
/// animation completes, when a new transition starts, and on <see cref="Dispose"/>; the page host's
/// transform/opacity are restored to their pre-animation values and every applied animation is
/// detached, so nothing lingers and no visual or event handler is retained.
/// </para>
/// <para>
/// <b>When it does not animate.</b> The mode policy lives in the WPF-free
/// <see cref="PageTransitionPlanner"/>: <c>none</c>, edit mode, the initial startup render, and a
/// page without a measured size all produce an instant plan and leave the layer empty. The OS
/// animation-effects preference never overrides an explicit animated mode. When the outgoing
/// snapshot cannot be captured, the selected animation still plays over the incoming page alone.
/// Animations are UI-only: nothing here activates the window, moves focus, or touches the
/// configuration.
/// </para>
/// </remarks>
public sealed class PageTransitionController : IDisposable
{
    /// <summary>Largest snapshot edge accepted, in pixels; larger panels animate the incoming page only.</summary>
    internal const int MaxSnapshotEdge = 8192;

    private readonly Grid _layer;
    private readonly Func<bool> _animationsAllowed;
    private readonly Func<bool> _systemAnimationsEnabled;
    private readonly Action<string>? _log;
    private readonly List<AppliedAnimation> _animations = [];

    private Image? _outgoing;
    private RenderTargetBitmap? _snapshot;
    private FrameworkElement? _incoming;
    private Transform? _previousTransform;
    private Point? _previousOrigin;
    private double _previousOpacity = 1;
    private PageTransitionPlan _plan = PageTransitionPlan.Instant;
    private TransitionBatch? _batch;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageTransitionController"/> class.
    /// </summary>
    /// <param name="layer">The hit-test-invisible overlay layer the outgoing snapshot is drawn in; must not be <see langword="null"/>.</param>
    /// <param name="animationsAllowed">Context gate (edit mode); <see langword="null"/> means allowed.</param>
    /// <param name="systemAnimationsEnabled">OS animation-effects probe; <see langword="null"/> reads
    /// <see cref="SystemParameters.ClientAreaAnimation"/>. Retained for the composition seam and
    /// tests, but it no longer disables playback: an explicit mode always wins.</param>
    /// <param name="log">Optional diagnostic sink; receives one Information-style line per page change.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="layer"/> is <see langword="null"/>.</exception>
    public PageTransitionController(
        Grid layer,
        Func<bool>? animationsAllowed = null,
        Func<bool>? systemAnimationsEnabled = null,
        Action<string>? log = null)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _animationsAllowed = animationsAllowed ?? (static () => true);
        _systemAnimationsEnabled = systemAnimationsEnabled ?? (static () => SystemParameters.ClientAreaAnimation);
        _log = log;
    }

    /// <summary>Gets a value indicating whether a transition animation is currently applied.</summary>
    public bool IsAnimating => _animations.Count > 0;

    /// <summary>Gets a value indicating whether the outgoing snapshot is still in the overlay layer.</summary>
    public bool HasOverlay => _layer.Children.Count > 0;

    /// <summary>Gets the plan of the last requested transition (animated or instant).</summary>
    public PageTransitionPlan LastPlan => _plan;

    /// <summary>Gets the animations applied to the running transition; exposed for the STA tests.</summary>
    internal IReadOnlyList<AppliedAnimation> ActiveAnimations => _animations;

    /// <summary>Gets the number of animations applied to the running transition; exposed for the STA tests.</summary>
    internal int ActiveAnimationCount => _animations.Count;

    /// <summary>Gets the overlay snapshot image while it is shown, or <see langword="null"/>; exposed for the STA tests.</summary>
    internal Image? OverlayForTest => _outgoing;

    /// <summary>Gets the installed incoming translate transform while animating; exposed for the STA tests.</summary>
    internal TranslateTransform? IncomingTranslateForTest { get; private set; }

    /// <summary>Gets the installed incoming scale transform while animating; exposed for the STA tests.</summary>
    internal ScaleTransform? IncomingScaleForTest { get; private set; }

    /// <summary>
    /// One animation applied to one target property, tracked so <see cref="Reset"/> can detach it.
    /// Exposed through <see cref="ActiveAnimations"/> for the STA tests, which assert the start/end
    /// values and easing.
    /// </summary>
    /// <param name="Target">The animated element or transform.</param>
    /// <param name="Property">The animated dependency property.</param>
    /// <param name="Animation">The animation applied to <paramref name="Target"/>.</param>
    internal readonly record struct AppliedAnimation(
        DependencyObject Target,
        DependencyProperty Property,
        AnimationTimeline Animation);

    /// <summary>
    /// Captures <paramref name="outgoing"/> and decides whether the transition animates. Must be
    /// called immediately before the page content is swapped, on the UI thread.
    /// </summary>
    /// <param name="outgoing">The page host (or page surface) to snapshot; must not be <see langword="null"/>.</param>
    /// <param name="kind">The configured transition kind.</param>
    /// <param name="direction">The page-change direction.</param>
    /// <param name="initial">Whether this is the initial startup render (never animated).</param>
    /// <returns>
    /// <see langword="true"/> when the plan animates, even if the outgoing snapshot could not be
    /// captured; <see cref="CompleteTransition"/> should then run after the swap so the incoming
    /// page still shows the selected transition.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outgoing"/> is <see langword="null"/>.</exception>
    public bool BeginTransition(
        FrameworkElement outgoing,
        PageTransitionKind kind,
        PageTransitionDirection direction,
        bool initial = false)
    {
        ArgumentNullException.ThrowIfNull(outgoing);

        if (_disposed)
        {
            return false;
        }

        Reset();

        var editMode = !_animationsAllowed();
        var width = outgoing.ActualWidth;
        var height = outgoing.ActualHeight;
        var skipReason = SkipReason(kind, width, height, editMode, initial);

        _plan = PageTransitionPlanner.Create(
            kind,
            direction,
            width,
            height,
            reducedMotion: !_systemAnimationsEnabled(),
            editMode: editMode,
            initial: initial);

        var snapshotCaptured = _plan.Animate && TryCapture(outgoing);
        Log(kind, direction, _plan.Animate, skipReason, snapshotCaptured);

        // The snapshot is best-effort: a failed capture no longer cancels the animation, because
        // CompleteTransition can animate the incoming page on its own.
        return _plan.Animate;
    }

    /// <summary>
    /// Starts the animation over the already-swapped <paramref name="incoming"/> page. No-op when
    /// <see cref="BeginTransition"/> did not arm an animation. When the outgoing snapshot was not
    /// captured, only the incoming page is animated.
    /// </summary>
    /// <param name="incoming">The page host (or page surface) now showing the new page; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="incoming"/> is <see langword="null"/>.</exception>
    public void CompleteTransition(FrameworkElement incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        if (_disposed || !_plan.Animate)
        {
            return;
        }

        try
        {
            _incoming = incoming;
            _batch = new TransitionBatch(Reset);
            ApplyPlan(_plan, incoming, _outgoing);

            if (_animations.Count == 0)
            {
                // Nothing to animate (should not happen for an animated plan): leave no transform,
                // animation or overlay behind.
                Reset();
            }
        }
        catch (Exception)
        {
            // A layout/render failure must never break a page change; the swap already happened.
            Reset();
        }
    }

    /// <summary>
    /// Detaches every applied animation (restoring the animated values), removes the overlay
    /// snapshot, releases the bitmap, and restores the page host's pre-animation
    /// transform/opacity. Safe to call repeatedly.
    /// </summary>
    public void Reset()
    {
        _batch?.Close();
        _batch = null;

        foreach (var applied in _animations)
        {
            try
            {
                // Detaching the animation also releases FillBehavior.HoldEnd, so the property falls
                // back to its base value before the transform/opacity below are restored.
                Stop(applied.Target, applied.Property);
            }
            catch (Exception)
            {
                // Cleanup is best-effort: one stubborn target must not keep the overlay alive.
            }
        }

        _animations.Clear();

        if (_outgoing is { } image)
        {
            image.Source = null;
            image.RenderTransform = null;
            _layer.Children.Remove(image);
            _outgoing = null;
        }

        _snapshot?.Clear();
        _snapshot = null;

        if (_incoming is { } incoming)
        {
            incoming.BeginAnimation(UIElement.OpacityProperty, null);
            incoming.Opacity = _previousOpacity;
            incoming.RenderTransformOrigin = _previousOrigin ?? default;
            incoming.RenderTransform = _previousTransform;
        }

        _incoming = null;
        _previousTransform = null;
        _previousOrigin = null;
        _previousOpacity = 1;
        IncomingTranslateForTest = null;
        IncomingScaleForTest = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        Reset();
    }

    /// <summary>
    /// Explains why a plan is instant, or <see langword="null"/> when it animates. The strings are
    /// the diagnostic reason tokens reported to the logging sink.
    /// </summary>
    /// <param name="kind">The configured transition kind.</param>
    /// <param name="width">The measured width in DIPs.</param>
    /// <param name="height">The measured height in DIPs.</param>
    /// <param name="editMode">Whether an edit session owns the panel.</param>
    /// <param name="initial">Whether this is the initial startup render.</param>
    /// <returns><c>mode-none</c>, <c>edit-mode</c>, <c>startup</c>, <c>no-size</c>, or <see langword="null"/>.</returns>
    internal static string? SkipReason(
        PageTransitionKind kind,
        double width,
        double height,
        bool editMode,
        bool initial)
    {
        if (kind == PageTransitionKind.None)
        {
            return "mode-none";
        }

        if (editMode)
        {
            return "edit-mode";
        }

        if (initial)
        {
            return "startup";
        }

        return PageTransitionPlanner.HasMeasuredSize(width, height) ? null : "no-size";
    }

    /// <summary>
    /// Formats the one-line diagnostic record for a page change.
    /// </summary>
    /// <param name="kind">The configured transition kind.</param>
    /// <param name="direction">The page-change direction.</param>
    /// <param name="animated">Whether the transition animates.</param>
    /// <param name="skipReason">The instant reason, or <see langword="null"/> when animated.</param>
    /// <param name="snapshotCaptured">Whether the outgoing snapshot was captured.</param>
    /// <returns>A single Information-style line (no Serilog template placeholders).</returns>
    internal static string FormatLogLine(
        PageTransitionKind kind,
        PageTransitionDirection direction,
        bool animated,
        string? skipReason,
        bool snapshotCaptured)
    {
        var line = $"Page transition: kind={kind} direction={direction} animated={(animated ? "true" : "false")} " +
            $"snapshot={(snapshotCaptured ? "captured" : "none")}";

        return skipReason is null ? line : $"{line} reason={skipReason}";
    }

    private void Log(
        PageTransitionKind kind,
        PageTransitionDirection direction,
        bool animated,
        string? skipReason,
        bool snapshotCaptured)
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            _log(FormatLogLine(kind, direction, animated, skipReason, snapshotCaptured));
        }
        catch (Exception)
        {
            // Diagnostics must never break a page change; a misbehaving sink is ignored.
        }
    }

    private bool TryCapture(FrameworkElement outgoing)
    {
        var width = (int)Math.Ceiling(outgoing.ActualWidth);
        var height = (int)Math.Ceiling(outgoing.ActualHeight);
        if (width <= 0 || height <= 0 || width > MaxSnapshotEdge || height > MaxSnapshotEdge)
        {
            return false;
        }

        try
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(outgoing);
            bitmap.Freeze();
            _snapshot = bitmap;

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                Focusable = false,
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            _outgoing = image;
            _layer.Children.Add(image);
            return true;
        }
        catch (Exception)
        {
            // Capturing is best-effort (software rendering quirks, disconnected visuals, oversized
            // surfaces): a failed snapshot means only the incoming page animates.
            _snapshot?.Clear();
            _snapshot = null;
            return false;
        }
    }

    /// <summary>
    /// Installs the incoming page's transform/opacity and applies one animation per changing value,
    /// each targeting its <see cref="Animatable"/> directly (see the type remarks).
    /// </summary>
    private void ApplyPlan(PageTransitionPlan plan, FrameworkElement incoming, Image? outgoing)
    {
        var duration = TimeSpan.FromMilliseconds(plan.DurationMilliseconds);
        var easing = CreateEasing(plan.Easing);

        var translate = new TranslateTransform(plan.IncomingOffsetX, plan.IncomingOffsetY);
        var scale = new ScaleTransform(plan.IncomingStartScale, plan.IncomingStartScale);
        IncomingTranslateForTest = translate;
        IncomingScaleForTest = scale;

        _previousTransform = incoming.RenderTransform;
        _previousOrigin = incoming.RenderTransformOrigin;
        _previousOpacity = incoming.Opacity;

        incoming.RenderTransformOrigin = new Point(0.5, 0.5);
        incoming.RenderTransform = new TransformGroup
        {
            Children = { scale, translate },
        };

        // Only the axes that actually change get a clock; a no-op animation would be wasted work.
        if (plan.IncomingOffsetX != 0)
        {
            Animate(translate, TranslateTransform.XProperty, plan.IncomingOffsetX, 0, duration, easing);
        }

        if (plan.IncomingOffsetY != 0)
        {
            Animate(translate, TranslateTransform.YProperty, plan.IncomingOffsetY, 0, duration, easing);
        }

        if (plan.IncomingStartOpacity != 1)
        {
            Animate(incoming, UIElement.OpacityProperty, plan.IncomingStartOpacity, 1, duration, easing);
        }

        if (plan.IncomingStartScale != 1)
        {
            Animate(scale, ScaleTransform.ScaleXProperty, plan.IncomingStartScale, 1, duration, easing);
            Animate(scale, ScaleTransform.ScaleYProperty, plan.IncomingStartScale, 1, duration, easing);
        }

        if (outgoing is null)
        {
            return;
        }

        var outgoingTranslate = new TranslateTransform();
        outgoing.RenderTransform = outgoingTranslate;

        if (plan.OutgoingOffsetX != 0)
        {
            Animate(outgoingTranslate, TranslateTransform.XProperty, 0, plan.OutgoingOffsetX, duration, easing);
        }

        if (plan.OutgoingOffsetY != 0)
        {
            Animate(outgoingTranslate, TranslateTransform.YProperty, 0, plan.OutgoingOffsetY, duration, easing);
        }

        if (plan.OutgoingEndOpacity != 1)
        {
            Animate(outgoing, UIElement.OpacityProperty, 1, plan.OutgoingEndOpacity, duration, easing);
        }
    }

    /// <summary>
    /// Applies one hold-end animation to a target property and tracks it for cleanup. All animations
    /// of a transition share one duration, so the last completion tears the transition down.
    /// </summary>
    private void Animate(
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration,
        IEasingFunction? easing)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            FillBehavior = FillBehavior.HoldEnd,
        };

        if (easing is not null)
        {
            animation.EasingFunction = easing;
        }

        var batch = _batch;
        batch?.Track();
        animation.Completed += (_, _) => batch?.Completed();

        switch (target)
        {
            case Animatable animatable:
                animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            case UIElement element:
                element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            default:
                throw new InvalidOperationException(
                    $"Cannot animate {property.OwnerType.Name}.{property.Name} on {target.GetType().Name}.");
        }

        _animations.Add(new AppliedAnimation(target, property, animation));
    }

    /// <summary>Detaches one animation from its target, releasing its hold-end value.</summary>
    private static void Stop(DependencyObject target, DependencyProperty property)
    {
        switch (target)
        {
            case Animatable animatable:
                animatable.BeginAnimation(property, null);
                break;
            case UIElement element:
                element.BeginAnimation(property, null);
                break;
        }
    }

    /// <summary>
    /// Counts the animations of one transition, so the last completion tears it down exactly once.
    /// WPF raises <see cref="Timeline.Completed"/> with the animation's <see cref="AnimationClock"/> as
    /// the sender (not the timeline), so the batch is captured by each handler instead of being
    /// identified from the event arguments. <see cref="Close"/> makes the completions of a transition
    /// that was already reset no-ops, so a tick in flight cannot tear down a newer page change.
    /// </summary>
    private sealed class TransitionBatch
    {
        private readonly Action _onCompleted;
        private int _pending;
        private bool _closed;

        public TransitionBatch(Action onCompleted) => _onCompleted = onCompleted;

        public void Track() => _pending++;

        public void Close() => _closed = true;

        public void Completed()
        {
            if (_closed)
            {
                return;
            }

            _pending--;

            if (_pending > 0)
            {
                return;
            }

            _closed = true;
            _onCompleted();
        }
    }

    /// <summary>
    /// Creates the easing curve for a plan; <see langword="null"/> means linear (WPF animations are
    /// linear without an easing function).
    /// </summary>
    private static IEasingFunction? CreateEasing(PageTransitionEasing easing) =>
        easing == PageTransitionEasing.EaseOutCubic
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : null;
}
