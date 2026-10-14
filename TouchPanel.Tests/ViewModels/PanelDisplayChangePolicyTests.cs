using TouchPanel.Config;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// T-08 part 2 (RF-017/RF-022): the pure display-change decision table. It verifies that the
/// configured monitor returning auto-shows a panel hidden because of its absence, that a
/// disappearing target hides (fallback <c>none</c>) or moves the visible panel to the primary
/// fallback with a notification (fallback <c>primary</c>, never silent), that a visible panel is
/// re-placed with fresh bounds on resolution/DPI changes, and that nothing happens when no
/// relevant state changed. No window, hardware, or user profile is touched.
/// </summary>
public sealed class PanelDisplayChangePolicyTests
{
    [Fact]
    public void ApplyDisplayChange_TargetReturnsWhileHiddenBecauseUnavailable_AutoShowsWithoutNotification()
    {
        var vm = new MainWindowViewModel();
        var unavailable = vm.RequestShow(Unavailable(MonitorFallback.None));
        Assert.Equal(PanelShowOutcome.StayHidden, unavailable.Outcome);
        Assert.True(vm.IsHiddenBecauseMonitorUnavailable);

        var target = Monitor("Touchscreen", bounds: new PixelRect(1920, 0, 1024, 600));
        var decision = vm.ApplyDisplayChange(Available(target, MonitorFallback.None));

        Assert.Equal(PanelDisplayChangeOutcome.Show, decision.Outcome);
        Assert.Same(target, decision.Monitor);
        Assert.False(decision.UsedFallback);
        Assert.False(decision.ShouldNotify);
        Assert.True(vm.IsPanelVisible);
        Assert.False(vm.IsHiddenBecauseMonitorUnavailable);
        Assert.True(vm.IsMonitorAvailable);
        Assert.Same(target, vm.TargetMonitor);
    }

    [Fact]
    public void ApplyDisplayChange_TargetReturnsWhileHiddenByUserRequest_DoesNothing()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Available(Monitor("Touchscreen"), MonitorFallback.None));
        vm.RequestHide();
        Assert.False(vm.IsHiddenBecauseMonitorUnavailable);

        var decision = vm.ApplyDisplayChange(Available(Monitor("Touchscreen"), MonitorFallback.None));

        Assert.Equal(PanelDisplayChangeOutcome.None, decision.Outcome);
        Assert.False(decision.ShouldNotify);
        Assert.False(vm.IsPanelVisible);
    }

    [Fact]
    public void ApplyDisplayChange_TargetDisappearsWhileVisible_HidesWithWarningNotification()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Available(Monitor("Touchscreen"), MonitorFallback.None));

        var decision = vm.ApplyDisplayChange(Unavailable(MonitorFallback.None));

        Assert.Equal(PanelDisplayChangeOutcome.Hide, decision.Outcome);
        Assert.True(decision.ShouldNotify);
        Assert.Equal(NotificationSeverity.Warning, decision.NotificationSeverity);
        Assert.False(vm.IsPanelVisible);
        Assert.True(vm.IsHiddenBecauseMonitorUnavailable);
        Assert.False(vm.UsedFallback);
    }

    [Fact]
    public void ApplyDisplayChange_TargetDisappearsWithPrimaryFallback_MovesToPrimaryAndNotifies()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Available(Monitor("Touchscreen"), MonitorFallback.Primary));

        var primary = Monitor("Primary", primary: true);
        var decision = vm.ApplyDisplayChange(Fallback(primary));

        // RF-017: the opted-in fallback keeps the panel usable and the placement is not silent.
        Assert.Equal(PanelDisplayChangeOutcome.Reposition, decision.Outcome);
        Assert.Same(primary, decision.Monitor);
        Assert.True(decision.UsedFallback);
        Assert.True(decision.ShouldNotify);
        Assert.Equal(NotificationSeverity.Warning, decision.NotificationSeverity);
        Assert.True(vm.IsPanelVisible);
        Assert.True(vm.UsedFallback);
        Assert.False(vm.IsMonitorAvailable);
        Assert.Same(primary, vm.TargetMonitor);
    }

    [Fact]
    public void ApplyDisplayChange_TargetStillMissingOnFallback_RepositionsWithoutRepeatedNotification()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Available(Monitor("Touchscreen"), MonitorFallback.Primary));
        vm.ApplyDisplayChange(Fallback(Monitor("Primary", primary: true, bounds: new PixelRect(0, 0, 1920, 1080))));

        // A resolution change on the fallback monitor: re-place with fresh bounds, no new balloon.
        var resizedPrimary = Monitor("Primary", primary: true, bounds: new PixelRect(0, 0, 2560, 1440));
        var decision = vm.ApplyDisplayChange(Fallback(resizedPrimary));

        Assert.Equal(PanelDisplayChangeOutcome.Reposition, decision.Outcome);
        Assert.Same(resizedPrimary, decision.Monitor);
        Assert.False(decision.ShouldNotify);
        Assert.True(vm.IsPanelVisible);
        Assert.True(vm.UsedFallback);
    }

    [Fact]
    public void ApplyDisplayChange_TargetReturnsWhileOnFallback_RepositionsOntoTargetWithoutNotification()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Fallback(Monitor("Primary", primary: true)));
        Assert.True(vm.UsedFallback);

        var target = Monitor("Touchscreen", bounds: new PixelRect(1920, 0, 1024, 600));
        var decision = vm.ApplyDisplayChange(Available(target, MonitorFallback.Primary));

        Assert.Equal(PanelDisplayChangeOutcome.Reposition, decision.Outcome);
        Assert.Same(target, decision.Monitor);
        Assert.False(decision.UsedFallback);
        Assert.False(decision.ShouldNotify);
        Assert.True(vm.IsPanelVisible);
        Assert.False(vm.UsedFallback);
        Assert.True(vm.IsMonitorAvailable);
    }

    [Fact]
    public void ApplyDisplayChange_ResolutionChangeWhileVisibleOnTarget_RepositionsWithFreshBounds()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Available(Monitor("Touchscreen", bounds: new PixelRect(1920, 0, 1024, 600)), MonitorFallback.None));

        var resized = Monitor("Touchscreen", bounds: new PixelRect(1920, 0, 1280, 800));
        var decision = vm.ApplyDisplayChange(Available(resized, MonitorFallback.None));

        Assert.Equal(PanelDisplayChangeOutcome.Reposition, decision.Outcome);
        Assert.Same(resized, decision.Monitor);
        Assert.False(decision.ShouldNotify);
        Assert.True(vm.IsPanelVisible);
    }

    [Fact]
    public void ApplyDisplayChange_FallbackWhileHidden_StaysHiddenAndDoesNotPlaceSilently()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Unavailable(MonitorFallback.Primary));
        Assert.True(vm.IsHiddenBecauseMonitorUnavailable);

        // The fallback is only applied on an explicit show request (tray/second launch) or when
        // the panel was already visible; a hidden panel must not pop onto the primary silently.
        var decision = vm.ApplyDisplayChange(Fallback(Monitor("Primary", primary: true)));

        Assert.Equal(PanelDisplayChangeOutcome.None, decision.Outcome);
        Assert.False(decision.ShouldNotify);
        Assert.False(vm.IsPanelVisible);
        Assert.True(vm.IsHiddenBecauseMonitorUnavailable);
    }

    [Fact]
    public void ApplyDisplayChange_NoMonitorWhileHidden_DoesNothing()
    {
        var vm = new MainWindowViewModel();

        var decision = vm.ApplyDisplayChange(Unavailable(MonitorFallback.None));

        Assert.Equal(PanelDisplayChangeOutcome.None, decision.Outcome);
        Assert.False(decision.ShouldNotify);
        Assert.False(vm.IsPanelVisible);
        Assert.Null(vm.TargetMonitor);
    }

    [Fact]
    public void ApplyDisplayChange_RepeatedHideEvents_NotifyOnlyOnce()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Available(Monitor("Touchscreen"), MonitorFallback.None));

        var first = vm.ApplyDisplayChange(Unavailable(MonitorFallback.None));
        var second = vm.ApplyDisplayChange(Unavailable(MonitorFallback.None));

        Assert.Equal(PanelDisplayChangeOutcome.Hide, first.Outcome);
        Assert.True(first.ShouldNotify);
        Assert.Equal(PanelDisplayChangeOutcome.None, second.Outcome);
        Assert.False(second.ShouldNotify);
    }

    [Fact]
    public void RequestHide_DisarmsTheAutomaticShow()
    {
        var vm = new MainWindowViewModel();
        vm.RequestShow(Unavailable(MonitorFallback.None));
        Assert.True(vm.IsHiddenBecauseMonitorUnavailable);

        vm.RequestHide();

        Assert.False(vm.IsHiddenBecauseMonitorUnavailable);
        Assert.Equal(
            PanelDisplayChangeOutcome.None,
            vm.ApplyDisplayChange(Available(Monitor("Touchscreen"), MonitorFallback.None)).Outcome);
    }

    [Fact]
    public void RequestShow_UpdatesThePendingAutoShowFlag()
    {
        var vm = new MainWindowViewModel();

        vm.RequestShow(Available(Monitor("Touchscreen"), MonitorFallback.None));
        Assert.False(vm.IsHiddenBecauseMonitorUnavailable);

        vm.RequestShow(Unavailable(MonitorFallback.None));
        Assert.True(vm.IsHiddenBecauseMonitorUnavailable);

        vm.RequestShow(Fallback(Monitor("Primary", primary: true)));
        Assert.False(vm.IsHiddenBecauseMonitorUnavailable);
    }

    private static MonitorDescriptor Monitor(
        string friendlyName,
        bool primary = false,
        PixelRect? bounds = null) => new()
        {
            FriendlyName = friendlyName,
            IsPrimary = primary,
            Bounds = bounds ?? new PixelRect(0, 0, 1920, 1080),
            WorkArea = bounds ?? new PixelRect(0, 0, 1920, 1080),
            SourceGdiName = primary ? @"\\.\DISPLAY1" : @"\\.\DISPLAY2",
        };

    private static MonitorSelectionResult Available(MonitorDescriptor monitor, MonitorFallback fallback) => new()
    {
        Monitor = monitor,
        IsAvailable = true,
        UsedFallback = false,
        Fallback = fallback,
        Reason = "matched the configured identity",
    };

    private static MonitorSelectionResult Unavailable(MonitorFallback fallback) => new()
    {
        Monitor = null,
        IsAvailable = false,
        UsedFallback = false,
        Fallback = fallback,
        Reason = "no monitor matched the configured identity",
    };

    private static MonitorSelectionResult Fallback(MonitorDescriptor primary) => new()
    {
        Monitor = primary,
        IsAvailable = false,
        UsedFallback = true,
        Fallback = MonitorFallback.Primary,
        Reason = "configured target unavailable; using the configured primary fallback",
    };
}
