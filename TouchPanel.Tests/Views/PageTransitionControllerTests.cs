using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Paging;
using TouchPanel.Views;

namespace TouchPanel.Tests.Views;

/// <summary>
/// STA coverage for the page-transition controller (user-requested addition). Each mode applies the
/// documented animations (duration, ease-out curve, start/end values) that actually move: the tests
/// pump the real dispatcher and assert that the incoming transform, the incoming opacity and the
/// outgoing overlay progress between their start and end values mid-flight, then that cleanup
/// restores the page host, detaches every animation and removes the overlay. Also covers the overlay
/// being non-interactive, an explicit mode animating even when the OS reports animations disabled, a
/// failed snapshot still animating the incoming page, and the diagnostic log line. No window is
/// shown; timing assertions are ranges, never fixed frames.
/// </summary>
public sealed class PageTransitionControllerTests
{
    /// <summary>Generous upper bound for one transition to finish (the plans are 200–260 ms).</summary>
    private const int TimeoutMilliseconds = 8000;

    [Fact]
    public async Task Slide_Forward_EntersFromTheRightAndPushesTheOldPageLeft()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);

            var started = controller.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Forward);
            var overlay = controller.OverlayForTest!;
            var overlayHitTestVisible = overlay.IsHitTestVisible;
            controller.CompleteTransition(host);

            var translate = controller.IncomingTranslateForTest!;
            var outgoing = (TranslateTransform)overlay.RenderTransform;
            var motion = PumpMotion(controller, translate, outgoing, overlay, host);

            return new
            {
                Started = started,
                LayerHitTestVisible = layer.IsHitTestVisible,
                OverlayHitTestVisible = overlayHitTestVisible,
                controller.LastPlan.DurationMilliseconds,
                motion.StartedAnimating,
                motion.Incoming,
                motion.Outgoing,
                motion.Final,
            };
        });

        Assert.True(state.Started);
        Assert.True(state.StartedAnimating);
        Assert.Equal(240, state.DurationMilliseconds);
        Assert.False(state.OverlayHitTestVisible);
        Assert.False(state.LayerHitTestVisible);

        // Mid-flight: the incoming page entered from the right (800) but has not arrived yet (0).
        Assert.Contains(state.Incoming, value => value > 0 && value < 800);

        // ...and the outgoing snapshot left its resting position towards the left (-800).
        Assert.Contains(state.Outgoing, value => value < 0 && value > -800);

        AssertCleanup(state.Final);
    }

    [Fact]
    public async Task Slide_Backward_EntersFromTheLeftAndPushesTheOldPageRight()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);

            Assert.True(controller.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Backward));
            var overlay = controller.OverlayForTest!;
            controller.CompleteTransition(host);

            var translate = controller.IncomingTranslateForTest!;
            var outgoing = (TranslateTransform)overlay.RenderTransform;
            var motion = PumpMotion(controller, translate, outgoing, overlay, host);

            return new
            {
                motion.StartedAnimating,
                motion.Incoming,
                motion.Outgoing,
                motion.Final,
            };
        });

        Assert.True(state.StartedAnimating);

        // Strictly between the -800 start offset and 0.
        Assert.Contains(state.Incoming, value => value < 0 && value > -800);

        // The old page slides the other way (to the right).
        Assert.Contains(state.Outgoing, value => value > 0 && value < 800);

        AssertCleanup(state.Final);
    }

    [Fact]
    public async Task SlideUp_RisesTheIncomingPageAndTheOldPageOutOfView()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);

            Assert.True(controller.BeginTransition(host, PageTransitionKind.SlideUp, PageTransitionDirection.Forward));
            var overlay = controller.OverlayForTest!;
            controller.CompleteTransition(host);

            var translate = controller.IncomingTranslateForTest!;
            var outgoing = (TranslateTransform)overlay.RenderTransform;
            var incomingY = new List<double>();
            var outgoingY = new List<double>();
            PumpUntilDone(controller, () =>
            {
                if (translate.HasAnimatedProperties)
                {
                    incomingY.Add(translate.Y);
                }

                if (outgoing.HasAnimatedProperties)
                {
                    outgoingY.Add(outgoing.Y);
                }

                return (false, 0d);
            });

            return new
            {
                IncomingY = incomingY,
                OutgoingY = outgoingY,
                Final = SampleFinal(controller, host, translate, overlay),
            };
        });

        // Incoming page: strictly between the 600 DIP start offset and 0.
        Assert.Contains(state.IncomingY, value => value > 0 && value < 600);

        // The (opaque) old page must rise off the top; holding it still hid the whole animation.
        Assert.Contains(state.OutgoingY, value => value < 0 && value > -600);

        AssertCleanup(state.Final);
    }

    [Fact]
    public async Task Zoom_SettlesTheScaleAndTheOpacity()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);

            Assert.True(controller.BeginTransition(host, PageTransitionKind.Zoom, PageTransitionDirection.Forward));
            var overlay = controller.OverlayForTest!;
            controller.CompleteTransition(host);

            var scale = controller.IncomingScaleForTest!;
            var translate = controller.IncomingTranslateForTest!;
            var opacity = new List<double>();
            var scales = new List<double>();
            PumpUntilDone(controller, () =>
            {
                if (host.HasAnimatedProperties)
                {
                    opacity.Add(host.Opacity);
                }

                if (scale.HasAnimatedProperties)
                {
                    scales.Add(scale.ScaleX);
                }

                return (false, 0d);
            });

            return new
            {
                Opacity = opacity,
                Scales = scales,
                Final = SampleFinal(controller, host, translate, overlay),
            };
        });

        // 0.96 → 1: strictly between the two.
        Assert.Contains(state.Scales, value => value > 0.96 && value < 1);

        // 0.35 → 1 on the incoming page.
        Assert.Contains(state.Opacity, value => value > 0 && value < 1);

        AssertCleanup(state.Final);
    }

    [Fact]
    public async Task Fade_CrossfadesTheIncomingPageIn()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);

            Assert.True(controller.BeginTransition(host, PageTransitionKind.Fade, PageTransitionDirection.Forward));
            var overlay = controller.OverlayForTest!;
            controller.CompleteTransition(host);

            var translate = controller.IncomingTranslateForTest!;
            var incomingOpacity = new List<double>();
            var outgoingOpacity = new List<double>();
            PumpUntilDone(controller, () =>
            {
                if (host.HasAnimatedProperties)
                {
                    incomingOpacity.Add(host.Opacity);
                }

                if (overlay.HasAnimatedProperties)
                {
                    outgoingOpacity.Add(overlay.Opacity);
                }

                return (false, 0d);
            });

            return new
            {
                IncomingOpacity = incomingOpacity,
                OutgoingOpacity = outgoingOpacity,
                Final = SampleFinal(controller, host, translate, overlay),
            };
        });

        Assert.Contains(state.IncomingOpacity, value => value > 0 && value < 1);
        Assert.Contains(state.OutgoingOpacity, value => value > 0 && value < 1);

        AssertCleanup(state.Final);
    }

    [Fact]
    public async Task Slide_AppliesTheDocumentedAnimations()
    {
        using var sta = new StaThread();

        var animations = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Forward));
            controller.CompleteTransition(host);
            return Describe(controller);
        });

        // Incoming translate X 800 → 0 and outgoing translate X 0 → -800; nothing else.
        Assert.Equal(
            new[] { ("X", 800d, 0d), ("X", 0d, -800d) },
            animations.Select(entry => (entry.Property, entry.From, entry.To)).ToArray());
        Assert.All(animations, entry => Assert.Equal(TimeSpan.FromMilliseconds(240), entry.Duration));
        Assert.All(animations, entry => Assert.Equal("CubicEase/EaseOut", entry.Easing));
    }

    [Fact]
    public async Task Fade_AppliesTheCrossfadeAnimations()
    {
        using var sta = new StaThread();

        var animations = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.Fade, PageTransitionDirection.Forward));
            controller.CompleteTransition(host);
            return Describe(controller);
        });

        Assert.Equal(
            new[] { ("Opacity", 0d, 1d), ("Opacity", 1d, 0d) },
            animations.Select(entry => (entry.Property, entry.From, entry.To)).ToArray());
        Assert.All(animations, entry => Assert.Equal(TimeSpan.FromMilliseconds(200), entry.Duration));
    }

    [Fact]
    public async Task SlideUp_AppliesThePairedVerticalAnimations()
    {
        using var sta = new StaThread();

        var animations = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.SlideUp, PageTransitionDirection.Forward));
            controller.CompleteTransition(host);
            return Describe(controller);
        });

        // The incoming page rises from +600 while the outgoing snapshot rises to -600.
        Assert.Equal(
            new[] { ("Y", 600d, 0d), ("Y", 0d, -600d) },
            animations.Select(entry => (entry.Property, entry.From, entry.To)).ToArray());
        Assert.All(animations, entry => Assert.Equal(TimeSpan.FromMilliseconds(260), entry.Duration));
    }

    [Fact]
    public async Task Zoom_AppliesTheScaleAndOpacityAnimations()
    {
        using var sta = new StaThread();

        var animations = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.Zoom, PageTransitionDirection.Forward));
            controller.CompleteTransition(host);
            return Describe(controller);
        });

        Assert.Equal(4, animations.Count);
        Assert.Equal(1, animations.Count(entry => entry is { Property: "ScaleX", From: 0.96, To: 1 }));
        Assert.Equal(1, animations.Count(entry => entry is { Property: "ScaleY", From: 0.96, To: 1 }));
        Assert.Equal(1, animations.Count(entry => entry is { Property: "Opacity", From: 0.35, To: 1 }));
        Assert.Equal(1, animations.Count(entry => entry is { Property: "Opacity", From: 1, To: 0 }));
        Assert.All(animations, entry => Assert.Equal(TimeSpan.FromMilliseconds(220), entry.Duration));
    }

    [Fact]
    public async Task None_DoesNotCaptureOrAnimate()
    {
        using var sta = new StaThread();

        var (started, overlay, animations, animating) = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);
            var began = controller.BeginTransition(host, PageTransitionKind.None, PageTransitionDirection.Forward);
            controller.CompleteTransition(host);
            return (began, controller.HasOverlay, controller.ActiveAnimationCount, controller.IsAnimating);
        });

        Assert.False(started);
        Assert.False(overlay);
        Assert.Equal(0, animations);
        Assert.False(animating);
    }

    [Fact]
    public async Task SystemAnimationsDisabled_StillAnimatesTheExplicitMode()
    {
        using var sta = new StaThread();

        var snapshot = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => false);

            var started = controller.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Forward);
            controller.CompleteTransition(host);

            return new
            {
                Started = started,
                controller.HasOverlay,
                controller.IsAnimating,
                Animations = Describe(controller).Select(entry => (entry.From, entry.To)).ToList(),
            };
        });

        // The OS animation-effects preference no longer disables an explicit mode.
        Assert.True(snapshot.Started);
        Assert.True(snapshot.HasOverlay);
        Assert.True(snapshot.IsAnimating);
        Assert.Contains((800d, 0d), snapshot.Animations);
    }

    [Fact]
    public async Task EditModeAndInitialRender_SkipTheAnimation()
    {
        using var sta = new StaThread();

        var results = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var editMode = new PageTransitionController(layer, () => false, () => true);
            using var initial = new PageTransitionController(layer, () => true, () => true);

            return new[]
            {
                editMode.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Forward),
                initial.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Forward, initial: true),
            };
        });

        Assert.All(results, Assert.False);
    }

    [Fact]
    public async Task CaptureFailure_AnimatesTheIncomingPageOnlyAndCleansUp()
    {
        using var sta = new StaThread();

        // A surface above MaxSnapshotEdge is a deterministic capture failure: the measured size is
        // valid (so the plan still animates) but no snapshot can be rendered.
        var oversized = PageTransitionController.MaxSnapshotEdge + 100;

        var snapshot = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost(oversized, 600);
            var lines = new List<string>();
            using var controller = new PageTransitionController(layer, () => true, () => false, lines.Add);

            var started = controller.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Backward);
            var hadOverlay = controller.HasOverlay;
            controller.CompleteTransition(host);
            var animations = Describe(controller).Select(entry => (entry.From, entry.To)).ToList();
            var animating = controller.IsAnimating;
            var translate = controller.IncomingTranslateForTest!;
            var observed = PumpUntilDone(controller, () => (translate.HasAnimatedProperties, translate.X));

            return new
            {
                Started = started,
                HadOverlay = hadOverlay,
                Animating = animating,
                Animations = animations,
                Observed = observed,
                CleanedOverlay = controller.HasOverlay,
                AnimationCount = controller.ActiveAnimationCount,
                Overlay = controller.OverlayForTest,
                HostTransform = host.RenderTransform,
                host.Opacity,
                Lines = lines,
            };
        });

        Assert.True(snapshot.Started);
        Assert.False(snapshot.HadOverlay);
        Assert.True(snapshot.Animating);

        // Backward slide: the incoming page enters from the left. Without a snapshot there are no
        // outgoing-target animations, only the incoming translate.
        Assert.Equal([(-(double)oversized, 0d)], snapshot.Animations);
        Assert.Contains(snapshot.Observed, sample => sample.Animated && sample.Value < 0 && sample.Value > -oversized);

        Assert.False(snapshot.CleanedOverlay);
        Assert.Equal(0, snapshot.AnimationCount);
        Assert.Null(snapshot.Overlay);
        Assert.True(snapshot.HostTransform is null || snapshot.HostTransform.Value.IsIdentity);
        Assert.Equal(1, snapshot.Opacity);

        var line = Assert.Single(snapshot.Lines);
        Assert.Contains("animated=true", line);
        Assert.Contains("snapshot=none", line);
        Assert.DoesNotContain("reason=", line);
    }

    [Fact]
    public async Task Logging_ReportsTheReasonForEverySkip()
    {
        using var sta = new StaThread();

        var lines = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            var unmeasured = new Grid();
            var log = new List<string>();

            using var normal = new PageTransitionController(layer, () => true, () => false, log.Add);
            using var editMode = new PageTransitionController(layer, () => false, () => false, log.Add);

            Assert.False(normal.BeginTransition(host, PageTransitionKind.None, PageTransitionDirection.Forward));
            Assert.False(normal.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Forward, initial: true));
            Assert.False(normal.BeginTransition(unmeasured, PageTransitionKind.Fade, PageTransitionDirection.Backward));
            Assert.False(editMode.BeginTransition(host, PageTransitionKind.Zoom, PageTransitionDirection.Forward));

            return log;
        });

        Assert.Equal(4, lines.Count);
        Assert.Contains("reason=mode-none", lines[0]);
        Assert.Contains("reason=startup", lines[1]);
        Assert.Contains("reason=no-size", lines[2]);
        Assert.Contains("reason=edit-mode", lines[3]);
        Assert.All(lines, line => Assert.Contains("animated=false", line));
    }

    [Fact]
    public async Task Logging_ReportsTheAnimatedTransitionAndTheCapture()
    {
        using var sta = new StaThread();

        var lines = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            var log = new List<string>();
            using var controller = new PageTransitionController(layer, () => true, () => false, log.Add);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.SlideUp, PageTransitionDirection.Backward));
            return log;
        });

        var line = Assert.Single(lines);
        Assert.Contains("kind=SlideUp", line);
        Assert.Contains("direction=Backward", line);
        Assert.Contains("animated=true", line);
        Assert.Contains("snapshot=captured", line);
        Assert.DoesNotContain("reason=", line);
    }

    [Fact]
    public void FormatLogLine_IncludesTheReasonOnlyWhenInstant()
    {
        Assert.Equal(
            "Page transition: kind=Slide direction=Backward animated=false snapshot=none reason=startup",
            PageTransitionController.FormatLogLine(
                PageTransitionKind.Slide,
                PageTransitionDirection.Backward,
                animated: false,
                skipReason: "startup",
                snapshotCaptured: false));

        Assert.Equal(
            "Page transition: kind=Zoom direction=Forward animated=true snapshot=captured",
            PageTransitionController.FormatLogLine(
                PageTransitionKind.Zoom,
                PageTransitionDirection.Forward,
                animated: true,
                skipReason: null,
                snapshotCaptured: true));
    }

    [Fact]
    public async Task Cleanup_ReleasesTheOverlayBitmapAndRestoresTheHost()
    {
        using var sta = new StaThread();

        var snapshot = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            var controller = new PageTransitionController(layer, () => true, () => true);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Forward));
            controller.CompleteTransition(host);

            var transformWhileAnimating = host.RenderTransform;
            var translate = controller.IncomingTranslateForTest!;
            var animatedWhileRunning = translate.HasAnimatedProperties;
            controller.Reset();

            return new CleanupSnapshot(
                controller.HasOverlay,
                controller.ActiveAnimationCount == 0,
                controller.OverlayForTest is null,
                host.RenderTransform,
                host.Opacity,
                transformWhileAnimating is not null,
                animatedWhileRunning,
                translate.HasAnimatedProperties);
        });

        Assert.False(snapshot.HasOverlay);
        Assert.True(snapshot.AnimationsReleased);
        Assert.True(snapshot.OverlayReleased);
        Assert.True(snapshot.HostTransform is null || snapshot.HostTransform.Value.IsIdentity);
        Assert.Equal(1, snapshot.HostOpacity);
        Assert.True(snapshot.HadTransform);
        Assert.True(snapshot.AnimatedWhileRunning);
        Assert.False(snapshot.AnimatedAfterReset);
    }

    [Fact]
    public async Task Dispose_ReleasesAnAnimatingOverlay()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            var controller = new PageTransitionController(layer, () => true, () => true);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.Fade, PageTransitionDirection.Forward));
            controller.CompleteTransition(host);
            controller.Dispose();
            return (controller.HasOverlay, AnimationCount: controller.ActiveAnimationCount, Animating: controller.IsAnimating);
        });

        Assert.False(state.HasOverlay);
        Assert.Equal(0, state.AnimationCount);
        Assert.False(state.Animating);
    }

    [Fact]
    public async Task AnimationCompletion_ReleasesTheOverlayAndRestoresTheHost()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);
            var previousTransform = host.RenderTransform;
            Assert.True(controller.BeginTransition(host, PageTransitionKind.Fade, PageTransitionDirection.Forward));
            controller.CompleteTransition(host);

            // Exercise the production completion path (not only Reset/Dispose): the last animation's
            // Completed event must release the overlay and restore the host.
            PumpUntilDone(controller, static () => (false, 0d));

            return new
            {
                controller.HasOverlay,
                controller.IsAnimating,
                AnimationCount = controller.ActiveAnimationCount,
                controller.OverlayForTest,
                host.Opacity,
                HostTransform = host.RenderTransform,
                Previous = previousTransform,
            };
        });

        Assert.False(state.HasOverlay);
        Assert.False(state.IsAnimating);
        Assert.Equal(0, state.AnimationCount);
        Assert.Null(state.OverlayForTest);
        Assert.Equal(1, state.Opacity);
        Assert.Same(state.Previous, state.HostTransform);
    }

    [Fact]
    public async Task RepeatedPageChange_ReplacesThePreviousOverlay()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var (_, host, layer) = CreateHost();
            using var controller = new PageTransitionController(layer, () => true, () => true);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Forward));
            controller.CompleteTransition(host);
            Assert.True(controller.BeginTransition(host, PageTransitionKind.Slide, PageTransitionDirection.Backward));
            controller.CompleteTransition(host);

            // The first transition's animations are detached, so only the new ones are tracked.
            return (Children: layer.Children.Count, controller.ActiveAnimationCount);
        });

        Assert.Equal(1, state.Children);

        // Backward slide: the incoming X plus the outgoing X of the replacement transition.
        Assert.Equal(2, state.ActiveAnimationCount);
    }

    /// <summary>
    /// Pumps the real dispatcher, sampling the incoming/outgoing horizontal offsets until the
    /// transition tears itself down, and returns the observed values plus the final state.
    /// </summary>
    private static MotionResult PumpMotion(
        PageTransitionController controller,
        TranslateTransform incoming,
        TranslateTransform outgoing,
        Image overlay,
        FrameworkElement host)
    {
        var incomingAxis = new List<double>();
        var outgoingAxis = new List<double>();
        var startedAnimating = false;

        PumpUntilDone(controller, () =>
        {
            startedAnimating |= incoming.HasAnimatedProperties;
            if (incoming.HasAnimatedProperties)
            {
                incomingAxis.Add(incoming.X);
            }

            if (outgoing.HasAnimatedProperties)
            {
                outgoingAxis.Add(outgoing.X);
            }

            return (false, 0d);
        });

        return new MotionResult(
            startedAnimating,
            incomingAxis,
            outgoingAxis,
            SampleFinal(controller, host, incoming, overlay));
    }

    /// <summary>Runs <paramref name="sample"/> on every dispatcher tick until the transition finishes.</summary>
    private static List<ObservedValue> PumpUntilDone(
        PageTransitionController controller,
        Func<(bool Animated, double Value)> sample)
    {
        var observed = new List<ObservedValue>();
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Render,
            (_, _) =>
            {
                var (animated, value) = sample();
                observed.Add(new ObservedValue(animated, value));

                if (!controller.IsAnimating)
                {
                    frame.Continue = false;
                }
            },
            Dispatcher.CurrentDispatcher);
        var guard = new DispatcherTimer(
            TimeSpan.FromMilliseconds(TimeoutMilliseconds),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);

        poll.Start();
        guard.Start();
        Dispatcher.PushFrame(frame);
        poll.Stop();
        guard.Stop();

        return observed;
    }

    /// <summary>Reads the state that must hold once a transition has been torn down.</summary>
    private static FinalState SampleFinal(
        PageTransitionController controller,
        FrameworkElement host,
        TranslateTransform translate,
        Image overlay) =>
        new(
            controller.HasOverlay,
            controller.IsAnimating,
            controller.ActiveAnimationCount,
            controller.OverlayForTest is null,
            host.Opacity,
            host.RenderTransform,
            translate.HasAnimatedProperties,
            overlay.HasAnimatedProperties);

    private static void AssertCleanup(FinalState state)
    {
        Assert.False(state.HasOverlay);
        Assert.False(state.Animating);
        Assert.Equal(0, state.AnimationCount);
        Assert.True(state.OverlayReleased);
        Assert.Equal(1, state.HostOpacity);
        Assert.True(state.HostTransform is null || state.HostTransform.Value.IsIdentity);
        Assert.False(state.IncomingAnimated);
        Assert.False(state.OutgoingAnimated);
    }

    /// <summary>Projects the applied animations into the values the structure assertions compare.</summary>
    private static List<(string Property, double From, double To, TimeSpan? Duration, string Easing)> Describe(
        PageTransitionController controller) =>
        controller.ActiveAnimations
            .Select(applied =>
            {
                var animation = Assert.IsType<DoubleAnimation>(applied.Animation);
                return (
                    applied.Property.Name,
                    animation.From ?? double.NaN,
                    animation.To ?? double.NaN,
                    animation.Duration.HasTimeSpan ? animation.Duration.TimeSpan : (TimeSpan?)null,
                    animation.EasingFunction switch
                    {
                        CubicEase cubic => $"CubicEase/{cubic.EasingMode}",
                        null => "Linear",
                        var other => other.GetType().Name,
                    });
            })
            .ToList();

    private static (Grid Root, Grid Host, Grid Layer) CreateHost(double width = 800, double height = 600)
    {
        var host = new Grid { Width = width, Height = height, Background = Brushes.Black };
        var layer = new Grid { Width = width, Height = height, IsHitTestVisible = false };
        var root = new Grid { Width = width, Height = height };
        root.Children.Add(host);
        root.Children.Add(layer);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return (root, host, layer);
    }

    private readonly record struct ObservedValue(bool Animated, double Value);

    private sealed record MotionResult(
        bool StartedAnimating,
        List<double> Incoming,
        List<double> Outgoing,
        FinalState Final);

    private sealed record FinalState(
        bool HasOverlay,
        bool Animating,
        int AnimationCount,
        bool OverlayReleased,
        double HostOpacity,
        Transform? HostTransform,
        bool IncomingAnimated,
        bool OutgoingAnimated);

    private sealed record CleanupSnapshot(
        bool HasOverlay,
        bool AnimationsReleased,
        bool OverlayReleased,
        Transform? HostTransform,
        double HostOpacity,
        bool HadTransform,
        bool AnimatedWhileRunning,
        bool AnimatedAfterReset);
}
