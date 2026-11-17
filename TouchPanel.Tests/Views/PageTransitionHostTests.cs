using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Paging;

namespace TouchPanel.Tests.Views;

/// <summary>
/// BAML/STA smoke coverage for the panel host with the page-transition layer (user-requested
/// addition): the window loads with the layer in place, startup and <c>none</c> never create an
/// overlay, the configured mode is applied to the window, and the transition layer cannot intercept
/// input. The window is never shown, so no desktop state is touched.
/// </summary>
public sealed class PageTransitionHostTests
{
    [Fact]
    public async Task MainWindow_ExposesTheTransitionLayerAndDefaultsToSlide()
    {
        using var sta = new StaThread();

        var snapshot = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                var layer = Assert.IsType<Grid>(window.FindName("PageTransitionLayer"));
                return new
                {
                    LayerHitTestVisible = layer.IsHitTestVisible,
                    LayerFocusable = layer.Focusable,
                    window.PageTransition,
                    window.PageTransitionForTest.HasOverlay,
                };
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(snapshot.LayerHitTestVisible);
        Assert.False(snapshot.LayerFocusable);
        Assert.Equal(PageTransitionKind.Slide, snapshot.PageTransition);
        Assert.False(snapshot.HasOverlay);
    }

    [Fact]
    public async Task ShowPage_WithoutAnimation_LeavesNoOverlay()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                window.PageTransition = PageTransitionKind.Slide;
                window.ShowPage(null, PageTransitionDirection.Forward, animate: false);

                var controller = window.PageTransitionForTest;
                return new
                {
                    controller.HasOverlay,
                    controller.IsAnimating,
                    controller.LastPlan.Animate,
                };
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(state.HasOverlay);
        Assert.False(state.IsAnimating);
        Assert.False(state.Animate);
    }

    [Fact]
    public async Task ShowPage_WithNoneMode_NeverAnimates()
    {
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                window.PageTransition = PageTransitionKind.None;
                var lines = new List<string>();
                window.PageTransitionLog = lines.Add;
                window.ShowPage(null, PageTransitionDirection.Backward, animate: true);

                var controller = window.PageTransitionForTest;
                return new
                {
                    controller.HasOverlay,
                    controller.IsAnimating,
                    controller.LastPlan.Kind,
                    controller.LastPlan.Direction,
                    controller.LastPlan.Animate,
                    Lines = lines,
                };
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(state.HasOverlay);
        Assert.False(state.IsAnimating);
        Assert.Equal(PageTransitionKind.None, state.Kind);
        Assert.Equal(PageTransitionDirection.Backward, state.Direction);
        Assert.False(state.Animate);

        // The window forwards its diagnostic sink to the controller: one line per page change.
        var line = Assert.Single(state.Lines);
        Assert.Contains("reason=mode-none", line);
        Assert.Contains("animated=false", line);
    }

    [Fact]
    public async Task ShowPage_WithGateClosedInEditMode_SkipsTheAnimation()
    {
        using var sta = new StaThread();

        var plan = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                window.PageTransition = PageTransitionKind.Zoom;
                window.PageTransitionsAllowed = () => false;
                window.ShowPage(null, PageTransitionDirection.Forward, animate: true);
                return window.PageTransitionForTest.LastPlan;
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(plan.Animate);
        Assert.Equal(PageTransitionKind.Zoom, plan.Kind);
    }

    [Fact]
    public async Task ShowPage_StartupRender_DoesNotAnimateEvenWhenAsked()
    {
        using var sta = new StaThread();

        var controllerState = await sta.InvokeAsync(() =>
        {
            var window = new MainWindow();
            try
            {
                window.PageTransition = PageTransitionKind.Fade;
                var controller = window.PageTransitionForTest;
                var started = controller.BeginTransition(
                    (FrameworkElement)window.FindName("PageHost"),
                    PageTransitionKind.Fade,
                    PageTransitionDirection.Forward,
                    initial: true);
                return (started, controller.HasOverlay);
            }
            finally
            {
                window.AllowExit = true;
                window.Close();
            }
        });

        Assert.False(controllerState.started);
        Assert.False(controllerState.HasOverlay);
    }
}
