using TouchPanel.Config;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Pure show/hide policy and state transitions for the panel window (T-08, RF-017/RF-020/RF-024):
/// the decision table for an available target, an unavailable target with <c>fallback: none</c>,
/// and an unavailable target with the primary fallback; the one-time close-to-tray notification
/// gate (RF-006) including its persistence through a temp <see cref="StateStore"/>; and the
/// topmost/edit-mode state mapping. No window, hardware, or user profile is touched.
/// </summary>
public sealed class MainWindowViewModelTests
{
    [Fact]
    public void RequestShow_AvailableTarget_ShowsWithoutNotification()
    {
        var vm = new MainWindowViewModel();
        var target = Monitor("DELL U2415", primary: false);

        var decision = vm.RequestShow(Available(target));

        Assert.Equal(PanelShowOutcome.Show, decision.Outcome);
        Assert.Same(target, decision.Monitor);
        Assert.False(decision.UsedFallback);
        Assert.False(decision.ShouldNotify);
        Assert.True(vm.IsPanelVisible);
        Assert.True(vm.IsMonitorAvailable);
        Assert.False(vm.UsedFallback);
        Assert.Same(target, vm.TargetMonitor);
    }

    [Fact]
    public void RequestShow_UnavailableWithFallbackNone_StaysHiddenWithNotification()
    {
        var vm = new MainWindowViewModel();

        var decision = vm.RequestShow(Unavailable(MonitorFallback.None));

        Assert.Equal(PanelShowOutcome.StayHidden, decision.Outcome);
        Assert.Null(decision.Monitor);
        Assert.False(decision.UsedFallback);
        Assert.True(decision.ShouldNotify);
        Assert.Equal(NotificationSeverity.Warning, decision.NotificationSeverity);
        Assert.False(vm.IsPanelVisible);
        Assert.False(vm.IsMonitorAvailable);
        Assert.Null(vm.TargetMonitor);
        Assert.Equal(MonitorFallback.None, vm.FallbackPolicy);
    }

    [Fact]
    public void RequestShow_UnavailableWithPrimaryFallback_ShowsOnPrimaryWithNotification()
    {
        var vm = new MainWindowViewModel();
        var primary = Monitor("Primary", primary: true);

        var decision = vm.RequestShow(Fallback(primary));

        Assert.Equal(PanelShowOutcome.Show, decision.Outcome);
        Assert.Same(primary, decision.Monitor);
        Assert.True(decision.UsedFallback);
        Assert.True(decision.ShouldNotify);
        Assert.Equal(NotificationSeverity.Warning, decision.NotificationSeverity);
        Assert.True(vm.IsPanelVisible);
        Assert.False(vm.IsMonitorAvailable);
        Assert.True(vm.UsedFallback);
        Assert.Same(primary, vm.TargetMonitor);
        Assert.Equal(MonitorFallback.Primary, vm.FallbackPolicy);
    }

    [Fact]
    public void RequestShow_ShowRequestInEveryState_FollowsTheSamePolicy()
    {
        // The tray and second-launch show requests call RequestShow with a fresh selection; the
        // table is therefore identical to the startup path (RF-017/RF-024).
        var vm = new MainWindowViewModel();
        var target = Monitor("Touchscreen", primary: false);
        var primary = Monitor("Primary", primary: true);

        Assert.Equal(PanelShowOutcome.Show, vm.RequestShow(Available(target)).Outcome);
        vm.RequestHide();
        Assert.Equal(PanelShowOutcome.StayHidden, vm.RequestShow(Unavailable(MonitorFallback.None)).Outcome);
        Assert.Equal(PanelShowOutcome.Show, vm.RequestShow(Fallback(primary)).Outcome);
    }

    [Fact]
    public void RequestHide_AfterShow_MarksHiddenAndKeepsTheResolvedTarget()
    {
        var vm = new MainWindowViewModel();
        var target = Monitor("Touchscreen", primary: false);
        vm.RequestShow(Available(target));

        vm.RequestHide();

        Assert.False(vm.IsPanelVisible);
        Assert.Same(target, vm.TargetMonitor);
    }

    [Fact]
    public void RequestShow_AfterHide_ShowsAgain()
    {
        var vm = new MainWindowViewModel();
        var target = Monitor("Touchscreen", primary: false);
        vm.RequestShow(Available(target));
        vm.RequestHide();

        var decision = vm.RequestShow(Available(target));

        Assert.Equal(PanelShowOutcome.Show, decision.Outcome);
        Assert.True(vm.IsPanelVisible);
    }

    [Fact]
    public void ApplyWindowOptions_MapsAlwaysOnTopFromConfiguration()
    {
        var vm = new MainWindowViewModel();

        Assert.False(vm.AlwaysOnTop);

        vm.ApplyWindowOptions(new WindowConfig { AlwaysOnTop = true });
        Assert.True(vm.AlwaysOnTop);

        vm.ApplyWindowOptions(new WindowConfig { AlwaysOnTop = false });
        Assert.False(vm.AlwaysOnTop);
    }

    [Fact]
    public void SetAlwaysOnTop_ReportsOnlyActualChanges()
    {
        var vm = new MainWindowViewModel();

        Assert.True(vm.SetAlwaysOnTop(true));
        Assert.True(vm.AlwaysOnTop);
        Assert.False(vm.SetAlwaysOnTop(true));
        Assert.True(vm.SetAlwaysOnTop(false));
        Assert.False(vm.AlwaysOnTop);
    }

    [Fact]
    public void ApplyMonitorPolicy_NormalizesUnknownFallbackToNone()
    {
        var vm = new MainWindowViewModel();

        vm.ApplyMonitorPolicy(new MonitorConfig { Fallback = MonitorFallback.Unknown });
        Assert.Equal(MonitorFallback.None, vm.FallbackPolicy);

        vm.ApplyMonitorPolicy(new MonitorConfig { Fallback = MonitorFallback.Primary });
        Assert.Equal(MonitorFallback.Primary, vm.FallbackPolicy);
    }

    [Fact]
    public async Task IsEditMode_DefaultsFalseAndFollowsTheAttachedEditModeService()
    {
        // T-12: the T-08 placeholder is replaced by the real service; the view model only forwards
        // the flag, and exiting restores the non-activating normal-mode state (RF-018/RF-026).
        var vm = new MainWindowViewModel();
        Assert.False(vm.IsEditMode);
        Assert.Null(vm.EditModeService);

        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets = [new LabelWidgetConfig { Id = "l1", X = 0, Y = 0, Width = 200, Height = 40 }],
                },
            ],
        };
        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
        var service = new EditModeService(config, page, new RecordingConfigStore());

        vm.AttachEditModeService(service);
        Assert.False(vm.IsEditMode);

        service.Enter(1024, 600);
        Assert.True(vm.IsEditMode);

        await service.ExitAsync();
        Assert.False(vm.IsEditMode);
    }

    [Fact]
    public void AttachEditModeService_RejectsNull()
    {
        var vm = new MainWindowViewModel();

        Assert.Throws<ArgumentNullException>(() => vm.AttachEditModeService(null!));
    }

    [Fact]
    public void TryConsumeCloseNotification_IsTrueOnlyOncePerState()
    {
        var vm = new MainWindowViewModel();

        Assert.True(vm.TryConsumeCloseNotification());
        Assert.False(vm.TryConsumeCloseNotification());
        Assert.False(vm.TryConsumeCloseNotification());
    }

    [Fact]
    public async Task TryConsumeCloseNotification_PersistedThroughStateStore_SurvivesRestart()
    {
        using var temp = new TempDirectory();
        var store = new StateStore(temp.Config);

        var state = await store.LoadAsync();
        var firstRun = new MainWindowViewModel(state);

        Assert.True(firstRun.TryConsumeCloseNotification());
        Assert.False(firstRun.TryConsumeCloseNotification());
        Assert.True(await store.SaveAsync(state));

        // A restarted process loads the persisted flag and must not notify again (RF-006).
        var reloaded = await new StateStore(temp.Config).LoadAsync();
        var secondRun = new MainWindowViewModel(reloaded);

        Assert.True(reloaded.CloseNotificationShown);
        Assert.False(secondRun.TryConsumeCloseNotification());
    }

    [Fact]
    public async Task ApplyRuntimeState_ReplacesTheGateSource()
    {
        using var temp = new TempDirectory();
        var persisted = await new StateStore(temp.Config).LoadAsync();
        persisted.CloseNotificationShown = true;

        var vm = new MainWindowViewModel();
        Assert.True(vm.TryConsumeCloseNotification());

        vm.ApplyRuntimeState(persisted);
        Assert.False(vm.TryConsumeCloseNotification());
    }

    private static MonitorDescriptor Monitor(string friendlyName, bool primary) => new()
    {
        FriendlyName = friendlyName,
        IsPrimary = primary,
        Bounds = new PixelRect(0, 0, 1920, 1080),
        WorkArea = new PixelRect(0, 0, 1920, 1040),
        SourceGdiName = primary ? @"\\.\DISPLAY1" : @"\\.\DISPLAY2",
    };

    private static MonitorSelectionResult Available(MonitorDescriptor monitor) => new()
    {
        Monitor = monitor,
        IsAvailable = true,
        UsedFallback = false,
        Fallback = MonitorFallback.None,
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
