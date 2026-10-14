using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Display;
using TouchPanel.Services.Health;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Services;

/// <summary>
/// T-08 part 2 (RF-017/RF-022/RF-024): applies the display-change policy end to end with the
/// selection service's fake enumerator, a fake <see cref="IPanelWindow"/>, and a recording
/// notifier — no STA, no real window, no OS event. Covers auto-show on monitor return, hide with
/// notification on target loss, non-silent fallback repositioning, fresh-bounds re-placement,
/// runtime-only widget clamping into the new DIP page bounds (RF-022), no-ops, and failure
/// isolation through <see cref="SubsystemHealth"/>.
/// </summary>
public sealed class PanelDisplayChangeHandlerTests
{
    [Fact]
    public async Task Handle_TargetReturnsWhileHidden_AutoShowsOnTargetWithoutNotification()
    {
        var config = Config(MonitorFallback.None);
        var harness = Create(config);
        var refused = harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        Assert.Equal(PanelShowOutcome.StayHidden, refused.Outcome);

        var target = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1024, 600));
        harness.Enumerator.Monitors.Add(target);

        await harness.Handler.HandleAsync();

        var placed = Assert.Single(harness.Window.Placements);
        Assert.Same(target, placed);
        Assert.Equal(1, harness.Window.ShowCalls);
        Assert.Equal([false], harness.Window.TopmostCalls);
        Assert.Empty(harness.Notifier.Notifications);
        Assert.True(harness.ViewModel.IsPanelVisible);
        Assert.False(harness.ViewModel.IsHiddenBecauseMonitorUnavailable);
    }

    [Fact]
    public async Task Handle_TargetDisappearsWhileVisibleWithFallbackNone_HidesAndNotifies()
    {
        var config = Config(MonitorFallback.None);
        var target = Monitor("DEL-A0BC");
        var harness = Create(config, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;

        harness.Enumerator.Monitors.Clear();

        await harness.Handler.HandleAsync();

        Assert.Equal(1, harness.Window.HideCalls);
        Assert.Empty(harness.Window.Placements);
        var notification = Assert.Single(harness.Notifier.Notifications);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.False(harness.ViewModel.IsPanelVisible);
        Assert.True(harness.ViewModel.IsHiddenBecauseMonitorUnavailable);
    }

    [Fact]
    public async Task Handle_TargetDisappearsWhileVisibleWithPrimaryFallback_MovesToPrimaryAndNotifies()
    {
        var config = Config(MonitorFallback.Primary);
        var target = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1024, 600));
        var primary = Monitor("ACR-1234", primary: true, bounds: new PixelRect(0, 0, 1920, 1080));
        var harness = Create(config, target, primary);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;
        Assert.Empty(harness.Notifier.Notifications);

        harness.Enumerator.Monitors.Remove(target);

        await harness.Handler.HandleAsync();

        var placed = Assert.Single(harness.Window.Placements);
        Assert.Same(primary, placed);
        Assert.Equal(0, harness.Window.HideCalls);
        var notification = Assert.Single(harness.Notifier.Notifications);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.True(harness.ViewModel.IsPanelVisible);
        Assert.True(harness.ViewModel.UsedFallback);
        Assert.Same(primary, harness.ViewModel.TargetMonitor);
    }

    [Fact]
    public async Task Handle_RepeatedChangeWhileAlreadyOnFallback_RepositionsWithoutNotifyingAgain()
    {
        var config = Config(MonitorFallback.Primary);
        var target = Monitor("DEL-A0BC");
        var primary = Monitor("ACR-1234", primary: true, bounds: new PixelRect(0, 0, 1920, 1080));
        var harness = Create(config, target, primary);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;
        harness.Enumerator.Monitors.Remove(target);
        await harness.Handler.HandleAsync();
        Assert.Single(harness.Notifier.Notifications);

        // A second burst for the same state must not duplicate the balloon.
        await harness.Handler.HandleAsync();

        Assert.Equal(2, harness.Window.Placements.Count);
        Assert.Single(harness.Notifier.Notifications);
    }

    [Fact]
    public async Task Handle_ResolutionChangeWhileVisibleOnTarget_RepositionsWithFreshBounds()
    {
        var config = Config(MonitorFallback.None);
        var target = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1024, 600));
        var harness = Create(config, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;

        var resized = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1280, 800));
        harness.Enumerator.Monitors.Clear();
        harness.Enumerator.Monitors.Add(resized);

        await harness.Handler.HandleAsync();

        var placed = Assert.Single(harness.Window.Placements);
        Assert.Equal(new PixelRect(1920, 0, 1280, 800), placed.Bounds);
        Assert.Empty(harness.Notifier.Notifications);
        Assert.True(harness.ViewModel.IsMonitorAvailable);
    }

    [Fact]
    public async Task Handle_WhenThePanelIsUserHidden_DoesNothing()
    {
        var config = Config(MonitorFallback.None);
        var target = Monitor("DEL-A0BC");
        var harness = Create(config, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.ViewModel.RequestHide();

        await harness.Handler.HandleAsync();

        Assert.Empty(harness.Window.Placements);
        Assert.Equal(0, harness.Window.ShowCalls);
        Assert.Equal(0, harness.Window.HideCalls);
        Assert.Empty(harness.Notifier.Notifications);
    }

    [Fact]
    public async Task Handle_WhenTheConfigurationIsNotLoadedYet_DoesNothing()
    {
        var harness = Create(config: null);

        var exception = await Record.ExceptionAsync(() => harness.Handler.HandleAsync());

        Assert.Null(exception);
        Assert.Empty(harness.Window.Placements);
        Assert.Empty(harness.Notifier.Notifications);
    }

    [Fact]
    public async Task Handle_WhenTheConfigAccessorThrows_IsIsolatedAndMarksTheSubsystemDegraded()
    {
        var sink = new RecordingSink();
        var logger = CreateLogger(sink);
        var health = new SubsystemHealth(logger);
        var handler = new PanelDisplayChangeHandler(
            new MainWindowViewModel(),
            new MonitorSelectionService(new FakeMonitorEnumerator(), logger),
            () => throw new InvalidOperationException("configuration read failed"),
            new FakePanelWindow(),
            new RecordingNotifier(),
            health,
            logger);

        var exception = await Record.ExceptionAsync(() => handler.HandleAsync());

        Assert.Null(exception);
        Assert.True(health.IsDegraded("display"));
    }

    [Fact]
    public async Task Handle_WhenAWindowOperationThrows_IsIsolatedAndMarksTheSubsystemDegraded()
    {
        var config = Config(MonitorFallback.None);
        var target = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1024, 600));
        var harness = Create(config, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;
        harness.Window.PlaceException = new InvalidOperationException("SetWindowPos failed");

        var resized = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1280, 800));
        harness.Enumerator.Monitors.Clear();
        harness.Enumerator.Monitors.Add(resized);

        var exception = await Record.ExceptionAsync(() => harness.Handler.HandleAsync());

        Assert.Null(exception);
        Assert.True(harness.Health.IsDegraded("display"));
    }

    [Fact]
    public async Task Handle_SuccessfulChange_MarksTheSubsystemHealthy()
    {
        var config = Config(MonitorFallback.None);
        var target = Monitor("DEL-A0BC");
        var harness = Create(config, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;

        await harness.Handler.HandleAsync();

        var status = harness.Health.GetStatus("display");
        Assert.NotNull(status);
        Assert.Equal(SubsystemState.Healthy, status!.State);
    }

    [Fact]
    public async Task Handle_ResolutionShrinkWhileVisible_ClampsWidgetsIntoTheNewPageBounds()
    {
        // RF-022/F-03: after the target monitor shrinks, an off-page widget would be unreachable on
        // a touchscreen unless it is clamped into the fresh page bounds.
        var config = Config(MonitorFallback.None, Label("w1", x: 900, y: 0, width: 200, height: 100));
        var page = CreatePage(config);
        var target = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1024, 600));
        var harness = CreateCore(config, page, editMode: null, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;
        var widget = Assert.Single(page.Widgets);
        Assert.Equal(900, widget.X);

        var resized = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 800, 500));
        harness.Enumerator.Monitors.Clear();
        harness.Enumerator.Monitors.Add(resized);

        await harness.Handler.HandleAsync();

        Assert.Equal(600, widget.X);
        Assert.Equal(0, widget.Y);
        Assert.Equal(200, widget.Width);
        Assert.Equal(100, widget.Height);
        Assert.True(widget.X + widget.Width <= 800);
        Assert.True(widget.Y + widget.Height <= 500);
    }

    [Fact]
    public async Task Handle_ResolutionChange_WidgetsInsideThePage_AreUntouched()
    {
        var config = Config(MonitorFallback.None, Label("w1", x: 10, y: 20, width: 100, height: 50));
        var page = CreatePage(config);
        var target = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1024, 600));
        var harness = CreateCore(config, page, editMode: null, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;

        var resized = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 800, 500));
        harness.Enumerator.Monitors.Clear();
        harness.Enumerator.Monitors.Add(resized);

        await harness.Handler.HandleAsync();

        var widget = Assert.Single(page.Widgets);
        Assert.Equal(10, widget.X);
        Assert.Equal(20, widget.Y);
        Assert.Equal(100, widget.Width);
        Assert.Equal(50, widget.Height);
    }

    [Fact]
    public async Task Handle_ResolutionShrink_ConvertsPhysicalBoundsToDipsBeforeClamping()
    {
        // RF-022: page bounds are monitor physical size ÷ DPI scale; at 200% a 3840 px wide monitor
        // is a 1920 DIP page, so a widget ending at 2000 DIP must be pulled fully inside.
        var config = Config(MonitorFallback.None, Label("w1", x: 1900, y: 0, width: 100, height: 100));
        var page = CreatePage(config);
        var target = Monitor("DEL-A0BC", bounds: new PixelRect(0, 0, 3840, 2160));
        var harness = CreateCore(config, page, editMode: null, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;
        harness.Window.DpiScale = 2d;

        await harness.Handler.HandleAsync();

        var widget = Assert.Single(page.Widgets);
        Assert.Equal(1820, widget.X);
        Assert.True(widget.X + widget.Width <= 1920);
    }

    [Fact]
    public async Task Handle_Clamping_IsRuntimeOnlyAndDoesNotTriggerASave()
    {
        // RF-022/RF-026: the implicit clamp must never persist; only explicit edit-mode saves do.
        var config = Config(MonitorFallback.None, Label("w1", x: 900, y: 0, width: 200, height: 100));
        var page = CreatePage(config);
        var store = new RecordingConfigStore();
        var editMode = new EditModeService(config, page, store);
        var target = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 1024, 600));
        var harness = CreateCore(config, page, editMode, target);
        harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        harness.Window.IsVisible = true;

        var resized = Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 800, 500));
        harness.Enumerator.Monitors.Clear();
        harness.Enumerator.Monitors.Add(resized);

        await harness.Handler.HandleAsync();

        var widget = Assert.Single(page.Widgets);
        Assert.Equal(600, widget.X);
        Assert.False(editMode.IsDirty);
        Assert.Equal(0, store.SaveCallCount);

        // The edit-mode clamp bounds follow the new page so a later drag cannot push widgets back out.
        Assert.Equal(800, editMode.PageWidth);
        Assert.Equal(500, editMode.PageHeight);

        // Even an explicit exit sees nothing dirty to persist.
        var result = await editMode.ExitAsync();
        Assert.False(result.Attempted);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task Handle_TargetReturnsWhileHidden_ClampsWidgetsIntoTheReturningMonitorBounds()
    {
        // RF-017/RF-022: the automatic show when the configured monitor returns also clamps, so a
        // widget laid out for a larger page is reachable on the returning panel.
        var config = Config(MonitorFallback.None, Label("w1", x: 900, y: 0, width: 200, height: 100));
        var page = CreatePage(config);
        var harness = CreateCore(config, page, editMode: null);
        var refused = harness.ViewModel.RequestShow(harness.Service.Select(config.Monitor));
        Assert.Equal(PanelShowOutcome.StayHidden, refused.Outcome);

        harness.Enumerator.Monitors.Add(Monitor("DEL-A0BC", bounds: new PixelRect(1920, 0, 800, 500)));

        await harness.Handler.HandleAsync();

        var widget = Assert.Single(page.Widgets);
        Assert.Equal(600, widget.X);
        Assert.True(widget.X + widget.Width <= 800);
    }

    private static PanelConfig Config(MonitorFallback fallback, params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Monitor = new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = "DEL-A0BC" },
            Fallback = fallback,
        },
        Pages =
        [
            new PageConfig { Id = "main", Widgets = [.. widgets] },
        ],
    };

    private static LabelWidgetConfig Label(string id, double x, double y, double width, double height) => new()
    {
        Id = id,
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Label = id,
    };

    private static PageViewModel CreatePage(PanelConfig config) =>
        PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

    private static MonitorDescriptor Monitor(
        string hardwareId,
        PixelRect? bounds = null,
        bool primary = false)
    {
        var separator = hardwareId.IndexOf('-', StringComparison.Ordinal);
        var manufacturer = hardwareId[..separator];
        var productCode = hardwareId[(separator + 1)..];
        var screen = bounds ?? new PixelRect(0, 0, 1920, 1080);

        return new MonitorDescriptor
        {
            Manufacturer = manufacturer,
            ProductCode = productCode,
            FriendlyName = $"{manufacturer} panel",
            IsPrimary = primary,
            Bounds = screen,
            WorkArea = screen,
            SourceGdiName = primary ? @"\\.\DISPLAY1" : @"\\.\DISPLAY2",
        };
    }

    private static ILogger CreateLogger(RecordingSink sink) =>
        new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();

    private static Harness Create(PanelConfig? config, params MonitorDescriptor[] monitors) =>
        CreateCore(config, page: null, editMode: null, monitors);

    private static Harness CreateCore(
        PanelConfig? config,
        PageViewModel? page,
        EditModeService? editMode,
        params MonitorDescriptor[] monitors)
    {
        var sink = new RecordingSink();
        var logger = CreateLogger(sink);
        var enumerator = new FakeMonitorEnumerator();
        enumerator.Monitors.AddRange(monitors);
        var service = new MonitorSelectionService(enumerator, logger);
        var viewModel = new MainWindowViewModel();
        if (editMode is not null)
        {
            viewModel.AttachEditModeService(editMode);
        }

        var window = new FakePanelWindow();
        var notifier = new RecordingNotifier();
        var health = new SubsystemHealth(logger);
        var handler = new PanelDisplayChangeHandler(
            viewModel,
            service,
            () => config,
            window,
            notifier,
            health,
            logger,
            pageAccessor: page is null ? null : () => page);

        return new Harness(handler, service, enumerator, window, notifier, viewModel, health, sink);
    }

    private sealed record Harness(
        PanelDisplayChangeHandler Handler,
        MonitorSelectionService Service,
        FakeMonitorEnumerator Enumerator,
        FakePanelWindow Window,
        RecordingNotifier Notifier,
        MainWindowViewModel ViewModel,
        SubsystemHealth Health,
        RecordingSink Sink);

    private sealed class FakeMonitorEnumerator : IMonitorEnumerator
    {
        public List<MonitorDescriptor> Monitors { get; } = [];

        public IReadOnlyList<MonitorDescriptor> Enumerate() => Monitors.ToArray();
    }

    private sealed class FakePanelWindow : IPanelWindow
    {
        public bool IsVisible { get; set; }

        public double DpiScale { get; set; } = 1d;

        public List<MonitorDescriptor> Placements { get; } = [];

        public List<bool> TopmostCalls { get; } = [];

        public int ShowCalls { get; private set; }

        public int HideCalls { get; private set; }

        public Exception? PlaceException { get; set; }

        public void PlaceOnMonitor(MonitorDescriptor monitor)
        {
            if (PlaceException is not null)
            {
                throw PlaceException;
            }

            Placements.Add(monitor);
        }

        public void SetTopmost(bool topmost) => TopmostCalls.Add(topmost);

        public void ShowWithoutActivation()
        {
            IsVisible = true;
            ShowCalls++;
        }

        public void HidePanel()
        {
            IsVisible = false;
            HideCalls++;
        }
    }

    private sealed class RecordingNotifier : IUserNotifier
    {
        public List<NotificationRecord> Notifications { get; } = [];

        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info) =>
            Notifications.Add(new NotificationRecord(title, message, severity));
    }

    private sealed record NotificationRecord(string Title, string Message, NotificationSeverity Severity);
}
