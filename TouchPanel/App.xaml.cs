using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using TouchPanel.Actions;
using TouchPanel.Actions.Handlers;
using TouchPanel.Config;
using TouchPanel.Config.ImportExport;
using TouchPanel.Input;
using TouchPanel.Interop;
using TouchPanel.Interop.Audio;
using TouchPanel.Interop.Input;
using TouchPanel.Interop.Monitors;
using TouchPanel.Monitoring;
using TouchPanel.Services;
using TouchPanel.Services.AppLifecycle;
using TouchPanel.Services.Appearance;
using TouchPanel.Services.Config;
using TouchPanel.Services.Display;
using TouchPanel.Services.Health;
using TouchPanel.Services.Logging;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;
using TouchPanel.Services.Processes;
using TouchPanel.Services.SingleInstance;
using TouchPanel.Services.Startup;
using TouchPanel.Services.Time;
using TouchPanel.Services.Tray;
using TouchPanel.Theme;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Hotkeys;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views;
using TouchPanel.Views.Edit;
using TouchPanel.Views.Hotkeys;
using TouchPanel.Views.Settings;
using TouchPanel.Widgets;

namespace TouchPanel;

/// <summary>
/// Application entry point and composition root. Initializes logging before any other subsystem,
/// wires the three global exception channels (RF-008..RF-012, DD-1), enforces a single instance
/// per Windows session (RF-001/RF-002), and drives the ordered shutdown through
/// <see cref="AppLifecycleCoordinator"/> (RF-007). Later tasks register their subsystems and
/// shutdown participants here.
/// </summary>
public partial class App : Application, IPanelConfigApplyTarget
{
    private ILogger _logger = Serilog.Log.Logger; // silent until OnStartup initializes logging
    private SingleInstanceService? _singleInstance;
    private AppLifecycleCoordinator? _lifecycle;
    private TrayService? _trayService;
    private TrayMenuHost? _trayMenuHost;
    private Task<ConfigLoadResult?>? _configLoadTask;
    private MainWindowViewModel? _mainWindowViewModel;
    private WidgetRegistry? _widgetRegistry;
    private PageViewModel? _pageViewModel;
    private MonitorSelectionService? _monitorSelectionService;
    private DisplayChangeWatcher? _displayChangeWatcher;
    private PanelDisplayChangeHandler? _displayChangeHandler;
    private MonitoringService? _monitoringService;
    private SensorHistoryStore? _sensorHistory;
    private SystemEventsClockStateSource? _clockStateSource;
    private StateStore? _stateStore;
    private RuntimeState? _runtimeState;
    private ToggleStatePersistence? _toggleStatePersistence;
    private JsonConfigStore? _configStore;
    private EditModeService? _editModeService;
    private PageSwitchCoordinator? _pageSwitchCoordinator;
    private PageSwitcherViewModel? _pageSwitcherViewModel;
    private HotKeyService? _hotKeyService;
    private ReloadDecisionEngine? _reloadEngine;
    private ConfigReloadController? _reloadController;
    private ConfigReloadPipeline? _reloadPipeline;
    private ConfigImportExport? _configImportExport;
    private PanelConfigApplier? _panelConfigApplier;
    private SettingsCoordinatorProvider? _settingsCoordinatorProvider;
    private SettingsWindow? _settingsWindow;
    private HotkeysEditorWindow? _hotkeysEditorWindow;
    private AppearanceState? _appearanceState;
    private ThemeManager? _themeManager;
    private ClockCultureSource? _clockCulture;

    /// <summary>Gets the resolved per-user paths (log and configuration directories).</summary>
    public AppPaths Paths { get; } = AppPaths.Default;

    /// <summary>
    /// Gets the configuration loaded once at primary startup (T-06), or <see langword="null"/>
    /// until the asynchronous load completes. Later startup steps (monitor selection T-07, panel
    /// window T-08) read this instead of loading <c>panel.json</c> themselves.
    /// </summary>
    public PanelConfig? Config { get; private set; }

    /// <summary>
    /// Gets the task that completes when the startup configuration load finished, carrying the
    /// classified RF-075 result (<see langword="null"/> when the load failed unexpectedly). Later
    /// startup steps can await it without blocking the UI thread.
    /// </summary>
    public Task<ConfigLoadResult?> ConfigLoadTask =>
        _configLoadTask ?? Task.FromResult<ConfigLoadResult?>(null);

    /// <summary>
    /// Gets the user-triggered configuration import/export service (T-12/RF-083), or
    /// <see langword="null"/> until the file-backed configuration is live. The settings window
    /// (T-17) calls <see cref="ConfigImportExport.ExportAsync"/>/<see cref="ConfigImportExport.ImportAsync"/>
    /// after it has obtained a path from a file picker; this task exposes no UI.
    /// </summary>
    public ConfigImportExport? ImportExport => _configImportExport;

    /// <summary>Gets the degraded-subsystem registry (RF-012/RNF-013), available after startup.</summary>
    public SubsystemHealth Health { get; private set; } = null!;

    /// <summary>
    /// Gets the RF-010 user-notification boundary used by later subsystems (startup registration,
    /// configuration, monitoring). Until the tray icon is registered it degrades to structured
    /// logging; after that, notifications are non-activating balloons (RF-077).
    /// </summary>
    public IUserNotifier Notifier { get; private set; } = new TrayNotifier(Serilog.Log.Logger);

    /// <summary>Initializes the application and wires the global exception handlers as early as possible.</summary>
    public App()
    {
        // Wire the global exception channels before anything else can fail (RF-011).
        // Until OnStartup replaces the logger, these use Serilog's silent logger;
        // the subscriptions themselves never throw.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // Logging is initialized before any other subsystem (DD-1). An unwritable log
        // directory degrades to trace output without blocking or crashing startup (RF-010).
        var initialization = LoggingSetup.CreateLogger(new LoggingOptions
        {
            LogDirectory = Paths.LogsDirectory,
        });

        _logger = initialization.Logger.ForContext<App>();
        Log.Logger = initialization.Logger;

        if (!initialization.FileLoggingEnabled)
        {
            Trace.WriteLine($"TouchPanel: file logging disabled ({initialization.FailureReason}).");
        }

        AppLog.Startup(_logger, Paths.LogsDirectory);

        // M4-RF-951/D-M4-5: capture the Clock display culture once here at the composition root and
        // supply the same instance to load-time validation (through the config store/reloader) and
        // to every Clock widget, so a custom format is validated and rendered in one culture.
        _clockCulture = new ClockCultureSource(CultureInfo.CurrentCulture);

        // Isolation contract used by later subsystems (telemetry, tray, config, actions).
        Health = new SubsystemHealth(_logger);

        // RF-010: log-only notification fallback until StartTray registers the icon and upgrades
        // this to a balloon presenter.
        Notifier = new TrayNotifier(_logger);

        // T-17/MD-10: the shared apply pipeline is stateless; it is created once here so hot reload
        // (T-11) and the settings/monitor-picker surfaces (T-17) all apply through the same target.
        _panelConfigApplier = new PanelConfigApplier(this, _logger);

        base.OnStartup(e);

        // T-03/RF-001/RF-002: exactly one instance per Windows session. The coordinator is
        // created first so the single-instance service is released through the ordered
        // shutdown on every path, including the secondary one that exits without a window.
        _lifecycle = new AppLifecycleCoordinator(_logger, AppLifecycleCoordinator.DefaultShutdownBudget);
        _lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "single-instance",
            _ =>
            {
                _singleInstance?.Dispose();
                return ValueTask.CompletedTask;
            }));

        // T-11/RF-082/M2-RF-828: stop hot reload first so no reload work runs once shutdown begins.
        // The watcher and its pending debounce timer are released before any later participant
        // writes panel.json/state.json (those writes remain self-suppressed by the decision engine).
        _lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "hot-reload",
            _ =>
            {
                _reloadPipeline?.Dispose();
                _reloadPipeline = null;
                _reloadController = null;
                return ValueTask.CompletedTask;
            }));

        // T-10/RF-007: release the rendered page (and its widget error timers) within the budget.
        _lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "widget-page",
            _ =>
            {
                _pageViewModel?.Dispose();
                _pageViewModel = null;
                return ValueTask.CompletedTask;
            }));

        // RF-046/M3-RF-463: release the app-level OS clock/state source after the page has released
        // its Clock subscriptions, so no system-event handler outlives the window.
        _lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "clock-state",
            _ =>
            {
                _clockStateSource?.Dispose();
                _clockStateSource = null;
                return ValueTask.CompletedTask;
            }));

        // T-11/RF-007/RF-066: stop telemetry polling and close the hardware library within the
        // shutdown budget, after the page has released its gauge subscriptions.
        _lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "monitoring",
            _ =>
            {
                _monitoringService?.Dispose();
                _monitoringService = null;
                return ValueTask.CompletedTask;
            }));

        // T-03/RF-069/RF-007: after polling has stopped, release the bounded-history buffers so no
        // sample references survive shutdown (M2-RF-697). Registered after `monitoring` so it runs
        // once the polling thread can no longer write.
        _lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "sensor-history",
            _ =>
            {
                _sensorHistory?.Dispose();
                _sensorHistory = null;
                return ValueTask.CompletedTask;
            }));

        // T-12/RF-007/RF-080: a layout edited but not yet exited is still persisted at shutdown
        // (save only when dirty, atomically; failures are contained by the service). F-08: when
        // that save fails, a best-effort recovery copy preserves the in-memory layout without
        // touching panel.json; the attempt shares the shutdown budget and never throws.
        _lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "edit-mode",
            async cancellationToken =>
            {
                var service = _editModeService;
                if (service is not null && (service.IsEditMode || service.IsDirty))
                {
                    var result = await service.ExitAsync(cancellationToken);
                    if (result is { Attempted: true, Succeeded: false } && !result.Canceled)
                    {
                        await TryWriteEmergencyRecoveryCopyAsync(cancellationToken);
                    }
                }
            }));

        // T-08/RF-045/M2-RF-453: flush any pending persisted toggle states and stop the debounce
        // timer within the shutdown budget. Registered before the state store is created; the
        // participant resolves the field at shutdown, after the startup load has wired it.
        _lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "toggle-state",
            async cancellationToken =>
            {
                var persistence = _toggleStatePersistence;
                _toggleStatePersistence = null;
                if (persistence is null)
                {
                    return;
                }

                await persistence.FlushAsync(cancellationToken);
                persistence.Dispose();
            }));

        var launchIntent = LaunchIntentParser.Parse(Environment.GetCommandLineArgs());
        _singleInstance = SingleInstanceService.Start(
            _logger,
            new SessionMutexLockFactory(),
            new Win32SingleInstanceChannel(),
            launchIntent);

        if (!_singleInstance.IsPrimary)
        {
            // RF-002/RF-006: the running instance was signaled (or signaling failed and was
            // logged); this process exits without creating a panel, tray, or window.
            _logger.Information("Startup: another instance is running; exiting without creating a window");
            Shutdown();
            return;
        }

        if (launchIntent == LaunchIntent.Exit)
        {
            // RF-006: --exit asked to stop the running instance, but this process acquired the
            // session lock, so no instance was running. Nothing to exit; never start the panel.
            _logger.Information("Startup: --exit was requested but no running instance was found; nothing to exit");
            Shutdown();
            return;
        }

        // RF-002: a second launch asks the running instance to show the panel. T-08 applies the
        // non-activating, monitor-availability policy through OnShowPanelRequested.
        _singleInstance.ShowPanelRequested += OnShowPanelRequested;

        // RF-006: a later `TouchPanel.exe --exit` asks this instance to run the same clean
        // shutdown as the tray Exit command.
        _singleInstance.ExitRequested += OnExitRequested;

        // T-10/RF-032/RF-050: the M1 action executor and the widget registry. Interactive widgets
        // only ever execute through the executor boundary (T-09), and the registry maps a config
        // type to its view model factory and template key (DD-6) with no per-type host logic.
        // M5-RF-521: the hotkey-reference action resolves the live configuration hotkeys.
        var hotkeyActionResolver = new HotkeyActionResolver(
            () => Config is null ? Array.Empty<HotkeyConfig>() : Config.Hotkeys);
        var actionExecutor = CreateActionExecutor(_logger, hotkeyActionResolver);

        // T-11/RF-062..RF-068: the application-level telemetry pipeline. The LHM adapter is the
        // only type that touches the hardware library; it opens lazily on the background polling
        // thread, never requests elevation (D-11), and degrades to unavailable values on failure.
        // It is started after the page is built (ApplyPanelStartup) and disposed by the shutdown
        // participant registered above.
        // T-03/RF-069: the bounded per-metric history (MD-5) is owned here and fed by the polling
        // loop; it is disposed by the `sensor-history` shutdown participant. The tracked-metric set
        // is reconciled by the later config-apply pipeline (T-11) as widgets change.
        _sensorHistory = new SensorHistoryStore(MonitoringService.DefaultInterval);

        // RF-046/M3-RF-463: one app-level clock/state source feeds every Clock widget so the clock
        // recomputes its next boundary on system time/zone, power, and session changes (no polling).
        // Released by the `clock-state` shutdown participant.
        _clockStateSource = new SystemEventsClockStateSource(_logger);

        // M3-RF-844/M3-RF-845: the shared active-appearance state (icon pack) the widget view models
        // read; the appearance apply step updates it live. The ThemeManager (theme + accent) is
        // created lazily on the UI thread the first time appearance is applied.
        _appearanceState = new AppearanceState();

        var sensorSource = new LibreHardwareMonitorAdapter(_logger);

        // T-05/RF-070/MD-7: a failed hardware backend is re-opened at most once per five minutes.
        // The retry runs on the background polling thread, is logged throttled, and never elevates.
        _monitoringService = new MonitoringService(
            sensorSource,
            TimeProvider.System,
            logger: _logger,
            health: Health,
            history: _sensorHistory,
            backendReinitializer: new BackendReinitializer(
                sensorSource.TryReinitialize,
                _logger,
                TimeProvider.System));

        _widgetRegistry = WidgetRegistry.CreateDefault(new WidgetCreationContext
        {
            ActionExecutor = actionExecutor,
            SnapshotSource = _monitoringService,
            History = _sensorHistory,
            RequestHistoryTracking = key => _sensorHistory?.Track(key),
            ClockStateSource = _clockStateSource,

            // M4-RF-951: the one canonical Clock culture captured at startup, shared with validation.
            ClockCulture = _clockCulture,

            // M3-RF-844/M3-RF-845: the active icon pack the widget view models resolve their glyphs
            // through; the appearance apply step updates it live.
            AppearanceSource = _appearanceState,

            // T-08/RF-045: persisted Toggle state. The field is assigned once the startup state
            // load completes (before the active page is built), so these resolve lazily; when it is
            // unavailable the toggles stay runtime-only exactly as in M1 (D-15).
            ToggleStateLookup = (pageId, widgetId) => _toggleStatePersistence?.TryGet(pageId, widgetId),
            ToggleStateChanged = (pageId, widgetId, isOn) => _toggleStatePersistence?.Set(pageId, widgetId, isOn),
            TimeProvider = TimeProvider.System,
            Dispatch = action => Dispatcher.BeginInvoke(action),
            Logger = _logger,
        });

        // T-11/M2-RF-823: the pure reload-decision engine owns the app self-write suppression
        // window. The stores below open a window around every save so the hot-reload watcher
        // (part 2) ignores the application's own writes. It performs no I/O and holds no handles.
        _reloadEngine = new ReloadDecisionEngine(TimeProvider.System);

        // T-06/RF-073: load panel.json once, off the UI thread (RNF-004). The store classifies
        // the four RF-075 states, generates first-run defaults, and recovers from corrupt or
        // future sources; the window and tray do not block on it.
        _configLoadTask = LoadConfigurationAsync();

        // T-07/T-08: monitor selection is evaluated on demand, never polled (RF-022); the same
        // service is reused by the startup path, the tray show request, and the second-launch
        // show request.
        _monitorSelectionService = new MonitorSelectionService(new Win32MonitorEnumerator(_logger), _logger);

        // T-08/RF-006/RF-018: the panel window is created hidden and only placed + shown without
        // activation after the configuration load resolves the target monitor (RF-017: stay
        // hidden and notify when the configured monitor is unavailable).
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.CloseToTrayRequested += OnCloseToTrayRequested;
        _mainWindowViewModel = new MainWindowViewModel();
        _stateStore = new StateStore(logger: _logger, writeSuppression: _reloadEngine);

        // T-05/RF-005/RF-013: tray presence, menu, and the RF-010 notification channel.
        StartTray(_lifecycle);

        // T-09/RF-058/RF-059: the global hotkey service (M2/MD-3). It is created after the tray so
        // its one aggregated failure notification uses the tray-backed channel, and its hidden
        // message window is created here on the UI thread (M2-RF-582). Hotkeys are applied once the
        // effective configuration is loaded (ApplyConfigurationStartup).
        StartHotKeys(actionExecutor, _lifecycle);

        // T-08/RF-022/RF-021: OS display-change events (and the window's own DPI changes) drive
        // on-demand monitor re-validation and physical re-placement; no polling (RNF-008).
        StartDisplayChangeHandling(_lifecycle, mainWindow, _mainWindowViewModel, _monitorSelectionService);

        // T-06/RF-003/RF-004 + T-08: once the load completes, apply the startup registration and
        // the panel's monitor/topmost/show policy; the tray above is ready to surface the
        // notifications those decisions may require.
        _ = CompleteConfigurationStartupAsync(_configLoadTask);
    }

    /// <summary>
    /// Creates the tray icon, its menu host and the notification channel, then registers their
    /// removal with the ordered shutdown (RF-007). Tray failures are isolated (RF-013): the panel
    /// and the rest of the application keep working without a tray icon.
    /// </summary>
    private void StartTray(AppLifecycleCoordinator lifecycle)
    {
        try
        {
            _trayService = new TrayService(
                new TrayIconAdapter(new TrayIcon()),
                _logger,
                TrayServiceOptions.Default with { IconHandle = StockIcon.LoadApplicationIcon() });
            _trayService.CommandSelected += OnTrayCommandSelected;

            // T-12/RF-005/RF-026: the checkable Edit Mode item after Show/Hide Panel; its checked
            // state is kept in sync by UpdateEditModeMenuItem.
            _trayService.SetMenuItem(
                new TrayMenuItem(TrayMenuCommand.EditMode, "Edit Mode", IsChecked: false, IsCheckable: true),
                index: 1);

            // T-17/RF-092: the tray-accessible Settings window, placed after Edit Mode and before the
            // configuration folder (M2-RF-921).
            _trayService.SetMenuItem(
                new TrayMenuItem(TrayMenuCommand.Settings, "Settings…"),
                index: 2);

            // M3/RF-061: the focused hotkey editor, placed after Settings and before the
            // configuration folder (M3-RF-611).
            _trayService.SetMenuItem(
                new TrayMenuItem(TrayMenuCommand.Hotkeys, "Hotkeys…"),
                index: 3);

            _trayMenuHost = new TrayMenuHost(_trayService);

            // RF-010/RF-077: balloons once the tray is registered, structured logging otherwise
            // (TrayNotifier falls back whenever ShowBalloon returns false).
            Notifier = new TrayNotifier(_logger, _trayService.ShowBalloon);

            // Registration failures inside Start retry with bounded backoff and recover on an
            // Explorer restart (RF-013); this call never throws.
            _trayService.Start();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Tray: initialization failed; continuing without a tray icon");

            // Release whatever was created before the failure; both Dispose implementations are
            // guaranteed not to throw (RF-013).
            _trayMenuHost?.Dispose();
            _trayMenuHost = null;
            _trayService?.Dispose();
            _trayService = null;
        }

        // RF-007: remove the tray icon within the shutdown budget. Registered after the
        // single-instance participant, which T-03 already owns.
        lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "tray",
            _ =>
            {
                _trayMenuHost?.Dispose();
                _trayMenuHost = null;
                _trayService?.Dispose();
                _trayService = null;
                return ValueTask.CompletedTask;
            }));
    }

    /// <summary>
    /// Creates the global hotkey service (T-09/RF-058/MD-3): the hidden UI-thread message window
    /// (Interop), the registration boundary, and the <see cref="HotKeyService"/> that parses the
    /// configuration and dispatches bound actions. Registration is isolated (RF-012/RNF-013): if the
    /// window or service cannot be created, hotkeys are disabled and the rest of the app continues.
    /// The service unmarshals and unregisters within the shutdown budget (M2-RF-595).
    /// </summary>
    /// <param name="actionExecutor">The executor a pressed hotkey dispatches its action through.</param>
    /// <param name="lifecycle">The shutdown coordinator that owns disposal.</param>
    private void StartHotKeys(IActionExecutor actionExecutor, AppLifecycleCoordinator lifecycle)
    {
        WindowsHotKeyRegistration? registration = null;
        try
        {
            registration = new WindowsHotKeyRegistration(WindowsHotKeyRegistration.DefaultWindowName);
            _hotKeyService = new HotKeyService(
                registration,
                actionExecutor,
                Notifier,
                _logger,
                action => Dispatcher.Invoke(action));

            _logger.Information("Hotkeys: the global hotkey service is ready");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hotkeys: initialization failed; global hotkeys are disabled");

            // Release the partially created window, then degrade to "hotkeys unavailable" (RF-012).
            _hotKeyService = null;
            try
            {
                registration?.Dispose();
            }
            catch (Exception disposeEx)
            {
                _logger.Warning(disposeEx, "Hotkeys: releasing the failed registration window failed");
            }
        }

        // RF-007/M2-RF-595: unregister all hotkeys and release the hidden window within the budget.
        lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "hotkeys",
            _ =>
            {
                _hotKeyService?.Dispose();
                _hotKeyService = null;
                return ValueTask.CompletedTask;
            }));
    }

    /// <summary>
    /// Applies the configured global hotkeys (T-09/RF-058): unregisters the current set and
    /// registers the effective one. Also the re-registration hook the later config-apply pipeline
    /// (T-11) uses on hot reload (M2-RF-595). Failures are isolated; the service handles per-entry
    /// failure handling and the one aggregated notification.
    /// </summary>
    /// <param name="config">The effective configuration whose hotkeys are applied.</param>
    private void ApplyHotKeys(PanelConfig config)
    {
        try
        {
            _hotKeyService?.Apply(config.Hotkeys);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hotkeys: applying the configured hotkeys failed");
        }
    }

    /// <summary>
    /// Wires live display-change handling (RF-022): the OS watcher (marshalled to the UI
    /// dispatcher) and the window's own <c>WM_DPICHANGED</c> event both feed the same handler,
    /// which re-evaluates the target monitor on demand and applies the RF-017 policy. The watcher
    /// is released through the ordered shutdown (RF-007); initialization failures disable the
    /// feature without taking the application down (RF-012/RNF-013).
    /// </summary>
    /// <param name="lifecycle">The shutdown coordinator that owns disposal.</param>
    /// <param name="window">The panel window.</param>
    /// <param name="viewModel">The panel state/policy owner.</param>
    /// <param name="selectionService">The on-demand monitor selection service.</param>
    private void StartDisplayChangeHandling(
        AppLifecycleCoordinator lifecycle,
        MainWindow window,
        MainWindowViewModel viewModel,
        MonitorSelectionService selectionService)
    {
        try
        {
            _displayChangeHandler = new PanelDisplayChangeHandler(
                viewModel,
                selectionService,
                () => Config,
                window,
                Notifier,
                Health,
                _logger,
                pageAccessor: () => _pageViewModel);

            // The watcher owns the SystemEvents subscription; the post delegate keeps subscriber
            // code (policy + SetWindowPos) on the UI thread.
            _displayChangeWatcher = new DisplayChangeWatcher(
                new SystemEventsDisplayChangeSource(),
                _logger,
                action => Dispatcher.BeginInvoke(action));
            _displayChangeWatcher.DisplayChanged += OnDisplayChanged;
            window.DpiConfigurationChanged += OnDisplayChanged;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Display change: initialization failed; live display-change handling is disabled");
            window.DpiConfigurationChanged -= OnDisplayChanged;
            _displayChangeWatcher?.Dispose();
            _displayChangeWatcher = null;
            _displayChangeHandler = null;
        }

        // RF-007: release the OS subscription within the shutdown budget. Registered after the
        // tray, which T-05 already owns.
        lifecycle.Register(new DelegateApplicationShutdownParticipant(
            "display-change",
            _ =>
            {
                if (MainWindow is MainWindow panel)
                {
                    panel.DpiConfigurationChanged -= OnDisplayChanged;
                }

                _displayChangeWatcher?.Dispose();
                _displayChangeWatcher = null;
                _displayChangeHandler = null;
                return ValueTask.CompletedTask;
            }));
    }

    /// <summary>
    /// Handles a marshalled display-change notification (OS display settings or the window's own
    /// DPI change). Fire-and-forget is safe: <see cref="PanelDisplayChangeHandler.HandleAsync"/>
    /// isolates every failure.
    /// </summary>
    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        var handler = _displayChangeHandler;
        if (handler is null)
        {
            return;
        }

        _ = handler.HandleAsync();
    }

    /// <summary>
    /// Loads <c>panel.json</c> through <see cref="JsonConfigStore"/> (RF-073..RF-081) running on
    /// the thread pool (RNF-004); the store performs all classification, recovery, and first-run
    /// generation. Operational failures are already handled by the store; this guard only ensures
    /// an unexpected exception never interrupts startup.
    /// </summary>
    private async Task<ConfigLoadResult?> LoadConfigurationAsync()
    {
        try
        {
            // The same single-writer store instance persists edit-mode layout changes (T-12/RF-080).
            // M4-RF-951: load-time validation uses the same canonical Clock culture as the widgets.
            _configStore = new JsonConfigStore(
                logger: _logger,
                writeSuppression: _reloadEngine,
                clockCulture: _clockCulture);
            return await _configStore.LoadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Configuration: the startup load failed unexpectedly");
            return null;
        }
    }

    /// <summary>
    /// Completes startup configuration handling once the load task finishes: stores the loaded
    /// configuration for later subsystems, surfaces recoverable non-current states to the user
    /// (RF-075/F-04/F-05), applies the per-user startup registration (RF-003/RF-004), resolves the
    /// target monitor and runtime state off the UI thread, and finally applies the panel's
    /// placement/show policy on the UI thread (T-08). Failures are isolated per phase.
    /// </summary>
    private async Task CompleteConfigurationStartupAsync(Task<ConfigLoadResult?> loadTask)
    {
        ConfigLoadResult? result = null;
        try
        {
            result = await loadTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Configuration: the startup load failed unexpectedly");
        }

        PanelStartupContext? panelStartup = null;
        try
        {
            // Guaranteed off the UI thread even when the load completed before this continuation
            // was attached, so registry access, monitor enumeration, state I/O, and notifications
            // never run on the dispatcher (RNF-004).
            panelStartup = await Task.Run(async () =>
            {
                ApplyConfigurationStartup(result);
                return await PreparePanelStartupAsync(result).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The composition root must never take the process down over configuration or
            // startup-registration handling (RF-004, RNF-013).
            _logger.Error(ex, "Configuration: applying the startup configuration failed unexpectedly");
            Notifier.Notify(
                "TouchPanel",
                "The configuration or the start-with-Windows setting could not be applied; TouchPanel continues running.",
                NotificationSeverity.Warning);
        }

        // T-08: window placement/showing is UI-thread work (WPF + SetWindowPos).
        if (panelStartup is { } startup)
        {
            try
            {
                await Dispatcher.InvokeAsync(() => ApplyPanelStartup(startup));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Panel: applying the startup panel state failed unexpectedly");
            }
        }
    }

    private void ApplyConfigurationStartup(ConfigLoadResult? result)
    {
        if (result?.Config is not { } config)
        {
            _logger.Error("Configuration: no effective configuration is available; the startup registration was not applied");
            Notifier.Notify(
                "TouchPanel",
                "The configuration could not be loaded; TouchPanel is running with built-in defaults and "
                + "the start-with-Windows setting was not applied.",
                NotificationSeverity.Error);
            return;
        }

        Config = config;
        _logger.Information(
            "Configuration: startup configuration ready. State={State} DetectedSchemaVersion={SchemaVersion} StartWithWindows={StartWithWindows}",
            result.State,
            result.DetectedSchemaVersion,
            config.Startup.StartWithWindows);

        // T-09/RF-058: register the global hotkeys from the effective configuration. Parsing is
        // cheap and the (thread-affine) registration is marshalled to the UI thread by the service,
        // so this call is safe from the background startup path.
        ApplyHotKeys(config);

        // RF-075/F-04/F-05: only the recovery states need user visibility; the store already
        // logged every diagnostic. A successful older-schema migration is an informational log.
        if (result.State is ConfigState.Invalid or ConfigState.UnsupportedFuture)
        {
            Notifier.Notify(
                "TouchPanel",
                BuildConfigurationRecoveredMessage(result.State),
                NotificationSeverity.Warning);
        }
        else if (HasSaveFailure(result))
        {
            // RF-077: a first-run default save or a migrated-save failure is not a recovery state,
            // but the user must still learn that the configuration was not persisted to disk.
            _logger.Warning("Configuration: a startup save failed; the in-memory configuration is in use");
            Notifier.Notify(
                "TouchPanel",
                "The configuration could not be saved to disk; TouchPanel is running with the in-memory "
                + "configuration and will retry on the next save or shutdown.",
                NotificationSeverity.Warning);
        }

        ApplyStartupRegistration(config);
    }

    /// <summary>
    /// Whether the load result carries an error from a save attempt (first-run defaults or a
    /// migrated configuration) that did not reach disk (RF-077).
    /// </summary>
    /// <param name="result">The classified load result.</param>
    /// <returns><see langword="true"/> when a save failure diagnostic is present.</returns>
    private static bool HasSaveFailure(ConfigLoadResult result) =>
        result.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == ConfigDiagnosticSeverity.Error
            && diagnostic.Code.StartsWith("config.save.", StringComparison.Ordinal));

    private static string BuildConfigurationRecoveredMessage(ConfigState state) => state switch
    {
        ConfigState.UnsupportedFuture =>
            "The configuration file was created by a newer version of TouchPanel and was left unchanged; "
            + "the last known valid configuration or defaults are in use.",
        _ =>
            "The configuration file could not be read and was preserved for diagnosis; "
            + "the last known valid configuration or defaults are in use.",
    };

    /// <summary>
    /// Resolves the target monitor (RF-016/RF-017) and loads the persisted runtime state
    /// (RF-006/RF-073) for the startup panel decision. Runs off the UI thread; every failure is
    /// isolated and produces "no panel state", which leaves the panel hidden (RF-012/RNF-013).
    /// </summary>
    /// <param name="result">The classified configuration load result, possibly without a config.</param>
    /// <returns>The data the UI thread needs to apply the startup panel state, or <see langword="null"/>.</returns>
    private async Task<PanelStartupContext?> PreparePanelStartupAsync(ConfigLoadResult? result)
    {
        if (result?.Config is not { } config)
        {
            _logger.Warning("Panel: no effective configuration is available; the panel stays hidden");
            return null;
        }

        var selectionService = _monitorSelectionService;
        var stateStore = _stateStore;
        if (selectionService is null || stateStore is null)
        {
            _logger.Warning("Panel: startup dependencies are not available; the panel stays hidden");
            return null;
        }

        MonitorSelectionResult selection;
        try
        {
            selection = selectionService.Select(config.Monitor);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Panel: monitor selection failed; the panel stays hidden");
            return null;
        }

        // state.json is non-fatal by design; a corrupt or missing file regenerates defaults.
        var runtimeState = await stateStore.LoadAsync().ConfigureAwait(false);

        // T-08 edge: publish the loaded state immediately (not only in ApplyPanelStartup) so the
        // close-notification flag can be persisted even when the panel startup path returns early.
        _runtimeState = runtimeState;

        // T-08/RF-045: own the persisted toggle-state map with a debounced, failure-tolerant
        // writer, and prune keys whose page/widget no longer exists (M2-RF-454). Replacing any
        // previous instance (reload) flushes and disposes it first.
        var previousPersistence = _toggleStatePersistence;
        _toggleStatePersistence = new ToggleStatePersistence(
            runtimeState,
            (snapshot, cancellationToken) => stateStore.SaveAsync(snapshot, cancellationToken),
            TimeProvider.System,
            logger: _logger);
        previousPersistence?.Dispose();
        _toggleStatePersistence.Prune(ToggleStatePersistence.CollectPersistentToggleKeys(config));

        return new PanelStartupContext(config, selection, runtimeState);
    }

    /// <summary>
    /// Applies the startup panel state on the UI thread (T-08): stores the loaded runtime state,
    /// applies the window/topmost and monitor policy to the view model, and either places + shows
    /// the panel without activation or stays hidden (RF-017/RF-018/RF-020/RF-024).
    /// </summary>
    /// <param name="context">The background-resolved startup data.</param>
    private void ApplyPanelStartup(PanelStartupContext context)
    {
        var viewModel = _mainWindowViewModel;
        if (viewModel is null || MainWindow is not MainWindow window)
        {
            return;
        }

        _runtimeState = context.RuntimeState;
        viewModel.ApplyRuntimeState(context.RuntimeState);
        viewModel.ApplyWindowOptions(context.Config.Window);
        viewModel.ApplyMonitorPolicy(context.Config.Monitor);

        // M3-RF-848: apply the configured theme, accent, and icon pack before the page is built so
        // every widget realizes with the active pack and the correct palette (runs on the UI thread).
        ApplyAppearanceCore(context.Config.Appearance);

        // T-10/RF-032/RF-037: render the active page of the loaded configuration in the panel.
        var page = AttachActivePage(window, context.Config);

        // T-12/RF-026/RF-039: edit mode needs the loaded configuration, the rendered page, and the
        // same single-writer store that performed the load (RF-080). It is created only when the
        // page exists; without a page there is nothing to edit.
        AttachEditMode(window, viewModel, context.Config, page);

        // T-16/RF-091: the in-panel page selector switches the active page without activation.
        AttachPageSwitcher(window, context.Config);

        // T-11/RF-064: start ~1 Hz telemetry once the gauges exist and are subscribed. Polling
        // continues while the panel is hidden (D-10); failures never block the panel or the UI.
        // T-04/RF-071: apply the configured interval (already clamped by validation) before the
        // loop starts so the first cadence is the configured one; later changes apply live.
        ApplyTelemetryInterval(context.Config.Telemetry);

        // T-11/RF-041/M2-RF-691: size the shared history to the active page's sparkline windows
        // after the interval is applied (so the interval coupling T-04 is preserved) and before the
        // loop records the first sample.
        ApplyHistoryWindow(context.Config);

        StartMonitoring();

        ApplyPanelShowDecision(window, viewModel.RequestShow(context.Selection), "startup");

        // T-11/RF-082: once the panel is live, begin watching panel.json so external edits apply.
        // Started only after the real file-backed load succeeded (never against a fake/foreign store).
        StartHotReload();
    }

    /// <summary>
    /// Starts the hot-reload watcher and apply pipeline once the panel is live (T-11/RF-082/MD-9).
    /// The watcher is established only when the effective configuration came through the real
    /// file-backed <see cref="JsonConfigStore"/> and an effective configuration exists, so a
    /// test/fake path or a failed startup never starts watching. Initialization failures are
    /// isolated: external edits are simply not applied live (RF-012/RNF-013).
    /// </summary>
    private void StartHotReload()
    {
        if (_reloadPipeline is not null || _reloadEngine is null)
        {
            return;
        }

        if (Config is null || _configStore is not JsonConfigStore)
        {
            _logger.Debug("Hot reload: not started because no file-backed effective configuration is loaded");
            return;
        }

        var store = _configStore;

        try
        {
            var reader = new PanelConfigCandidateReader(
                ConfigPaths.Default,
                logger: _logger,
                clockCulture: _clockCulture);
            var applier = _panelConfigApplier ??= new PanelConfigApplier(this, _logger);

            _reloadController = new ConfigReloadController(_reloadEngine, ConfigPaths.Default, TimeProvider.System, _logger);
            _reloadPipeline = new ConfigReloadPipeline(
                _reloadController,
                _reloadEngine,
                reader,
                applier,
                () => Config,
                ApplyReloadedConfiguration,
                Notifier,
                TimeProvider.System,
                Health,
                logger: _logger);
            _reloadPipeline.Start();

            // T-12/RF-083: import/export reuse the same reader, store, applier, and running-config
            // accessors as hot reload so a validated imported file takes the identical apply path.
            _configImportExport = new ConfigImportExport(
                store,
                applier,
                () => Config,
                ApplyReloadedConfiguration,
                Notifier,
                ConfigPaths.Default,
                reader,
                TimeProvider.System,
                _logger);

            _logger.Information("Hot reload: watching {PanelFile} for external changes", ConfigPaths.Default.PanelFile);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hot reload: initialization failed; external edits are not applied live");
            _reloadPipeline?.Dispose();
            _reloadPipeline = null;
            _reloadController = null;
        }
    }

    /// <summary>
    /// Publishes a validated externally edited configuration as the running one (T-11/M2-RF-822).
    /// Called by the reload pipeline after the whole candidate validated; the shared apply pipeline
    /// then pushes the values into the live subsystems.
    /// </summary>
    /// <param name="config">The validated candidate.</param>
    private void ApplyReloadedConfiguration(PanelConfig config) => Config = config;

    /// <summary>
    /// <see cref="IPanelConfigApplyTarget"/> (MD-10): applies the window options to the live panel,
    /// updating the <c>alwaysOnTop</c> Z-order on the UI thread. Called by the shared apply pipeline
    /// from the reload/apply context.
    /// </summary>
    /// <param name="window">The validated window section.</param>
    void IPanelConfigApplyTarget.ApplyWindowOptions(WindowConfig window)
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindowViewModel?.SetAlwaysOnTop(window.AlwaysOnTop) == true
                && MainWindow is MainWindow panel)
            {
                panel.SetTopmost(window.AlwaysOnTop);
            }
        });
    }

    /// <summary>
    /// <see cref="IPanelConfigApplyTarget"/> (MD-10): re-applies the monitor fallback policy and
    /// re-evaluates/re-places the panel through the existing display-change path, which performs
    /// the native monitor enumeration off the UI thread (RF-017/RF-022).
    /// </summary>
    /// <param name="monitor">The validated monitor section.</param>
    void IPanelConfigApplyTarget.ApplyMonitorTarget(MonitorConfig monitor)
    {
        Dispatcher.Invoke(() =>
        {
            _mainWindowViewModel?.ApplyMonitorPolicy(monitor);

            // Reuse the tested display-change handler: it re-evaluates the target monitor on demand
            // and applies the show/reposition/hide policy without activating the panel.
            if (_displayChangeHandler is { } handler)
            {
                _ = handler.HandleAsync();
            }
        });
    }

    /// <summary>
    /// <see cref="IPanelConfigApplyTarget"/> (MD-10/M2-RF-824): disposes the current active page
    /// view models and rebuilds them from the new configuration on the UI thread, then rebinds edit
    /// mode to the rebuilt page. The old page is disposed by <see cref="AttachActivePage"/> so no
    /// widget subscription leaks.
    /// </summary>
    /// <param name="config">The validated configuration whose active page is rendered.</param>
    void IPanelConfigApplyTarget.RebuildActivePage(PanelConfig config)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is not MainWindow window)
            {
                return;
            }

            var page = AttachActivePage(window, config);

            // T-16/M2-RF-824: keep the page selector in sync with the (possibly replaced) effective
            // configuration on every page rebuild (hot reload, import, or a page switch itself).
            _pageSwitcherViewModel?.ApplyConfiguration(config);

            var viewModel = _mainWindowViewModel;
            if (viewModel is not null && page is not null)
            {
                // Rebind edit mode to the rebuilt page/config; detach first so the handler is not
                // subscribed twice across reloads.
                window.EditModeExitRequested -= OnEditModeExitRequested;
                AttachEditMode(window, viewModel, config, page);
            }
        });
    }

    /// <summary>
    /// <see cref="IPanelConfigApplyTarget"/> (MD-10/RF-071): applies the telemetry polling interval
    /// live. Runs on the calling (background) path so the dispatcher is never blocked by the polling
    /// loop restart.
    /// </summary>
    /// <param name="telemetry">The validated telemetry section.</param>
    void IPanelConfigApplyTarget.ApplyTelemetryOptions(TelemetryConfig telemetry) =>
        ApplyTelemetryInterval(telemetry);

    /// <summary>
    /// <see cref="IPanelConfigApplyTarget"/> (MD-10/M2-RF-693): reconciles the bounded-history
    /// tracked-metric set against the rebuilt active page, and sizes the shared store's retained
    /// window to the maximum <c>windowSeconds</c> referenced by the active page's sparklines so each
    /// one can slice its own window (M2-RF-691/M2-RF-415).
    /// </summary>
    /// <param name="config">The validated configuration.</param>
    void IPanelConfigApplyTarget.ReconcileTrackedMetrics(PanelConfig config)
    {
        ApplyHistoryWindow(config);
        _sensorHistory?.SetTracked(PanelConfigApplier.CollectTrackedSensorKeys(config));
    }

    /// <summary>
    /// Sizes the shared bounded-history store to the maximum sparkline window referenced by the
    /// active configuration page, keeping the current (already applied/clamped) polling interval
    /// (T-04). Called at startup and by the apply pipeline on reload/page change; a failure is
    /// isolated so monitoring keeps its current retained capacity (RF-012/RNF-013).
    /// </summary>
    /// <param name="config">The validated configuration whose active page drives the window.</param>
    private void ApplyHistoryWindow(PanelConfig config)
    {
        if (_sensorHistory is not { } history)
        {
            return;
        }

        try
        {
            history.Reconfigure(PanelConfigApplier.CollectMaxSparklineWindowSeconds(config), history.Interval);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "History: applying the configured sparkline window failed; the retained capacity is kept");
        }
    }

    /// <summary>
    /// <see cref="IPanelConfigApplyTarget"/> (MD-10/M2-RF-595): unregisters and re-registers the
    /// global hotkeys from the new configuration.
    /// </summary>
    /// <param name="hotkeys">The configured hotkeys.</param>
    void IPanelConfigApplyTarget.ApplyHotKeys(IReadOnlyList<HotkeyConfig> hotkeys)
    {
        try
        {
            _hotKeyService?.Apply(hotkeys);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hotkeys: applying the configured hotkeys failed");
        }
    }

    /// <summary>
    /// <see cref="IPanelConfigApplyTarget"/> (MD-10/M2-RF-454): prunes persisted toggle state that
    /// the new configuration no longer references.
    /// </summary>
    /// <param name="config">The validated configuration.</param>
    void IPanelConfigApplyTarget.ReconcileToggleState(PanelConfig config) =>
        _toggleStatePersistence?.Prune(ToggleStatePersistence.CollectPersistentToggleKeys(config));

    /// <summary>
    /// <see cref="IPanelConfigApplyTarget"/> (MD-10/M3-RF-848): applies the global appearance
    /// (theme, accent, icon pack) live. Marshalled to the UI thread because it swaps application
    /// resource dictionaries; the <see cref="ThemeManager"/> restores the previous appearance
    /// wholesale on failure, and the whole step is isolated (M3-RF-848/F-M3-16/M3-RNF-138).
    /// </summary>
    /// <param name="appearance">The validated appearance section.</param>
    void IPanelConfigApplyTarget.ApplyAppearance(AppearanceConfig appearance)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyAppearanceCore(appearance);
            return;
        }

        Dispatcher.Invoke(() => ApplyAppearanceCore(appearance));
    }

    /// <summary>
    /// Applies theme + accent through <see cref="ThemeManager"/> (swap-with-wholesale-rollback) and
    /// publishes the icon pack to the shared <see cref="AppearanceState"/> so realized widgets
    /// re-resolve their glyphs without a rebuild (M3-RF-844/M3-RF-848). The pack is published only
    /// when the theme/accent swap succeeded, so a failed apply keeps the previous appearance
    /// wholesale (M3-RF-848/F-M3-16). Must run on the UI thread; every failure is logged and isolated
    /// so the panel keeps working (M3-RNF-138).
    /// </summary>
    /// <param name="appearance">The validated appearance section.</param>
    private void ApplyAppearanceCore(AppearanceConfig appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        try
        {
            // The ThemeManager restores the previous theme/accent wholesale on failure; the
            // AppearanceApplier publishes the icon pack only when that swap succeeded, so a failed
            // apply never leaves a partially-applied appearance (M3-RF-848/F-M3-16/M3-RNF-138).
            var applier = new AppearanceApplier(
                (themeId, accentColor) =>
                {
                    _themeManager ??= new ThemeManager(_logger);
                    return _themeManager.Apply(themeId, accentColor);
                },
                _appearanceState);

            applier.Apply(appearance);
        }
        catch (Exception ex)
        {
            // M3-RF-848/M3-RNF-138: an appearance failure must not affect the panel or the tray.
            _logger.Warning(ex, "Appearance: applying the configured theme/accent/icon pack failed; the current look is kept");
        }
    }

    /// <summary>
    /// Creates the M1 action executor with the production handlers (RF-048..RF-053): safe process
    /// launch, media keys, volume step/set, and mute. The executor contains every failure, logs it
    /// with redaction, and returns a result the widget surfaces (RF-036/RF-050).
    /// </summary>
    /// <param name="logger">Logger the executor attaches to action failures.</param>
    /// <param name="hotkeyResolver">
    /// The resolver the hotkey-reference handler uses to find the referenced hotkey's bound action
    /// (M5-RF-521); must not be <see langword="null"/>.
    /// </param>
    /// <returns>The composed executor; never <see langword="null"/>.</returns>
    private static IActionExecutor CreateActionExecutor(ILogger logger, IHotkeyActionResolver hotkeyResolver)
    {
        ArgumentNullException.ThrowIfNull(hotkeyResolver);

        var keyInput = new SendInputHelper();

        var coreHandlers = new IActionHandler[]
        {
            new RunCommandHandler(new SystemProcessLauncher()),
            new MediaKeyHandler(keyInput),
            new VolumeHandler(keyInput, new CoreAudioController()),
            new MuteHandler(keyInput),
            new SendKeysHandler(keyInput),
        };

        // M5-RF-521/M5-RNF-556: the hotkey handler dispatches the resolved action through a core
        // executor that does not register a hotkey handler, so a hotkey reference can never chain
        // into another hotkey reference.
        var coreExecutor = new ActionExecutor(coreHandlers, logger);
        var handlers = coreHandlers.Append(new RunHotkeyHandler(hotkeyResolver, coreExecutor));

        return new ActionExecutor(handlers, logger);
    }

    /// <summary>
    /// Builds the active page's view models through the widget registry (RF-032/RF-037) and assigns
    /// the page as the panel's data context so <c>WidgetPageHost</c> renders the configured widgets
    /// with absolute geometry. A build failure is isolated (RF-012/RNF-013) and leaves the panel
    /// without widgets instead of taking the application down or showing a half-built page.
    /// </summary>
    /// <param name="window">The panel window that receives the page as its data context.</param>
    /// <param name="config">The validated configuration whose active page is rendered.</param>
    /// <returns>The rendered page, or <see langword="null"/> when the page could not be built.</returns>
    private PageViewModel? AttachActivePage(MainWindow window, PanelConfig config)
    {
        var registry = _widgetRegistry;
        if (registry is null)
        {
            _logger.Warning("Widgets: the widget registry is unavailable; the panel renders without widgets");
            return null;
        }

        try
        {
            var page = PageViewModel.CreateForActivePage(config, registry);
            var previous = _pageViewModel;
            _pageViewModel = page;
            window.DataContext = page;
            previous?.Dispose();

            _logger.Information(
                "Widgets: the active page was built. PageId={PageId} Widgets={WidgetCount}",
                page.PageId,
                page.Widgets.Count);
            return page;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Widgets: building the active page failed; the panel renders without widgets");
            return null;
        }
    }

    /// <summary>
    /// Creates the edit-mode service for the rendered page and wires the T-12 seams (DD-7): the
    /// overlay receives the service as its data context, the window view model forwards the
    /// edit-mode flag, and the tray gains the checkable <b>Edit Mode</b> item (RF-005/RF-026).
    /// A missing store or page degrades to "edit mode unavailable" without affecting the panel.
    /// </summary>
    /// <param name="window">The panel window that hosts the edit overlay.</param>
    /// <param name="viewModel">The window state view model that forwards the edit-mode flag.</param>
    /// <param name="config">The effective configuration the page belongs to.</param>
    /// <param name="page">The rendered active page, or <see langword="null"/> when none was built.</param>
    private void AttachEditMode(MainWindow window, MainWindowViewModel viewModel, PanelConfig config, PageViewModel? page)
    {
        var store = _configStore;
        if (store is null || page is null)
        {
            _logger.Warning(
                "Edit mode: unavailable because the configuration store or the active page is missing. StoreAvailable={StoreAvailable} PageAvailable={PageAvailable}",
                store is not null,
                page is not null);
            return;
        }

        try
        {
            _editModeService = new EditModeService(config, page, store, _logger, Notifier, _widgetRegistry, _widgetRegistry, new ExecutableFilePicker(), _clockCulture, new SensorCatalog(_monitoringService), new ImageFilePicker());
            viewModel.AttachEditModeService(_editModeService);
            window.AttachEditModeService(_editModeService);
            window.EditModeExitRequested -= OnEditModeExitRequested;
            window.EditModeExitRequested += OnEditModeExitRequested;

            // UX fix #1: a long press on a widget in normal mode enters edit mode through the same
            // deliberate entry path as the tray item (the only path allowed to activate, D-6).
            window.EditModeEnterRequested -= OnEditModeEnterRequested;
            window.EditModeEnterRequested += OnEditModeEnterRequested;
            UpdateEditModeMenuItem();

            _logger.Information("Edit mode: ready. PageId={PageId}", page.PageId);
        }
        catch (Exception ex)
        {
            // Composition bugs must not take the panel down; edit mode is simply unavailable.
            _logger.Error(ex, "Edit mode: wiring the edit-mode service failed; edit mode is unavailable");
            _editModeService = null;
        }
    }

    /// <summary>
    /// Wires the in-panel page selector (T-16/RF-091/MD-15): builds the switch coordinator over the
    /// live configuration accessor, the single-writer store, and this composition root's apply
    /// target, then binds a <see cref="PageSwitcherViewModel"/> to the panel's selector. The
    /// coordinator is created once and reused across reloads; the view model is refreshed by
    /// <see cref="IPanelConfigApplyTarget.RebuildActivePage"/>. A missing store or a wiring failure
    /// degrades to "page switching unavailable" without affecting the panel (RF-012).
    /// </summary>
    /// <param name="window">The panel window that hosts the selector.</param>
    /// <param name="config">The effective configuration whose pages are offered.</param>
    private void AttachPageSwitcher(MainWindow window, PanelConfig config)
    {
        var store = _configStore;
        if (store is null)
        {
            _logger.Warning("Page switch: the configuration store is unavailable; the page selector is not wired");
            return;
        }

        try
        {
            _pageSwitchCoordinator ??= new PageSwitchCoordinator(
                () => Config,
                store,
                this,
                () => _editModeService?.IsEditMode != true,
                _logger);

            _pageSwitcherViewModel = new PageSwitcherViewModel(config, _pageSwitchCoordinator, _logger);
            window.AttachPageSwitcher(_pageSwitcherViewModel);

            _logger.Information("Page switch: the page selector is ready. PageCount={PageCount}", config.Pages.Count);
        }
        catch (Exception ex)
        {
            // A composition bug must not take the panel down; page switching is simply unavailable.
            _logger.Error(ex, "Page switch: wiring the page selector failed; page switching is unavailable");
            _pageSwitcherViewModel = null;
        }
    }

    /// <summary>
    /// Applies the configured telemetry polling interval to the monitoring service (RF-071/T-04).
    /// The value was clamped into <c>0.5–5 s</c> by <c>ConfigValidator</c>; <see cref="MonitoringService.ApplyInterval"/>
    /// clamps defensively as well. Called at startup and by the later config-apply pipeline (T-11);
    /// a failure is isolated so telemetry keeps running at the current cadence (RF-012/RNF-013).
    /// </summary>
    /// <param name="telemetry">The validated telemetry options from the effective configuration.</param>
    private void ApplyTelemetryInterval(TelemetryConfig telemetry)
    {
        try
        {
            _monitoringService?.ApplyInterval(TimeSpan.FromSeconds(telemetry.PollIntervalSeconds));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Monitoring: applying the configured telemetry interval failed; the current cadence is kept");
        }
    }

    /// <summary>
    /// Starts the ~1 Hz telemetry loop after the page has been built (RF-064/RF-066). The call only
    /// schedules the background task; the lazy hardware-library open happens on that task, never on
    /// the UI thread (RF-067). A start failure is isolated and leaves gauges showing unavailable
    /// values without affecting the rest of the panel (RF-012/RNF-013).
    /// </summary>
    private void StartMonitoring()
    {
        try
        {
            _monitoringService?.Start();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Monitoring: starting telemetry failed; gauges stay unavailable");
        }
    }

    /// <summary>
    /// Executes a <see cref="PanelShowDecision"/> on the panel window: place + non-activating show
    /// with the configured topmost state, or stay hidden; surfaces the decision's notification so
    /// a fallback placement or a refusal is never silent (RF-017/RF-018/RF-020/RF-024).
    /// </summary>
    /// <param name="window">The panel window.</param>
    /// <param name="decision">The pure decision from the view model.</param>
    /// <param name="source">Short source tag for logs ("startup", "tray", "second-launch").</param>
    private void ApplyPanelShowDecision(MainWindow window, PanelShowDecision decision, string source)
    {
        var topmost = _mainWindowViewModel?.AlwaysOnTop ?? false;

        if (decision is { Outcome: PanelShowOutcome.Show, Monitor: { } monitor })
        {
            window.PlaceOnMonitor(monitor);
            window.SetTopmost(topmost);
            window.ShowWithoutActivation();

            _logger.Information(
                "Panel: shown without activation. Source={Source} Monitor={Monitor} Fallback={Fallback} Topmost={Topmost}",
                source,
                monitor.DisplayName,
                decision.UsedFallback,
                topmost);
        }
        else
        {
            _logger.Warning("Panel: show request refused; the panel stays hidden. Source={Source}", source);
        }

        if (decision.ShouldNotify)
        {
            Notifier.Notify(
                decision.NotificationTitle ?? "TouchPanel",
                decision.NotificationMessage!,
                decision.NotificationSeverity);
        }
    }

    /// <summary>
    /// Applies <c>startup.startWithWindows</c> to the per-user HKCU Run entry with self-heal
    /// (RF-003/RF-004). Registry I/O runs off the UI thread; failures are logged and surfaced as a
    /// tray notification and never propagate (RF-004, RF-086).
    /// </summary>
    private void ApplyStartupRegistration(PanelConfig config)
    {
        var startWithWindows = config.Startup.StartWithWindows;

        StartupRegistrationResult result;
        try
        {
            var registration = new RegistryStartupRegistration(
                new HkcuRunRegistry(),
                Environment.ProcessPath,
                _logger);
            result = registration.Apply(startWithWindows);
        }
        catch (Exception ex)
        {
            // RegistryStartupRegistration never throws operationally; this guards composition bugs.
            _logger.Error(ex, "Startup registration: applying the configured state failed unexpectedly");
            Notifier.Notify(
                "TouchPanel",
                "The start-with-Windows setting could not be applied; TouchPanel continues running.",
                NotificationSeverity.Warning);
            return;
        }

        if (result.Succeeded)
        {
            return;
        }

        _logger.Error(
            "Startup registration: the configured state could not be applied. StartWithWindows={StartWithWindows} Code={Code} Message={Message}",
            startWithWindows,
            result.FailureCode,
            result.Message);

        Notifier.Notify(
            "TouchPanel",
            result.Message ?? "The start-with-Windows setting could not be applied; TouchPanel continues running.",
            NotificationSeverity.Warning);
    }

    private void OnTrayCommandSelected(object? sender, TrayMenuCommandEventArgs e)
    {
        try
        {
            switch (e.Command)
            {
                case TrayMenuCommand.ShowHidePanel:
                    TogglePanelVisibility();
                    break;
                case TrayMenuCommand.EditMode:
                    _ = SetEditModeAsync(_editModeService?.IsEditMode != true);
                    break;
                case TrayMenuCommand.Settings:
                    OpenSettings();
                    break;
                case TrayMenuCommand.Hotkeys:
                    OpenHotkeysEditor();
                    break;
                case TrayMenuCommand.OpenConfigurationFolder:
                    _ = OpenConfigurationFolderAsync();
                    break;
                case TrayMenuCommand.Exit:
                    RequestApplicationExit("tray");
                    break;
                default:
                    _logger.Warning("Tray: no handler for menu command {Command}", e.Command);
                    break;
            }
        }
        catch (Exception ex)
        {
            // The tray must never take the application down (RF-013).
            _logger.Warning(ex, "Tray: handling menu command {Command} failed", e.Command);
        }
    }

    private void TogglePanelVisibility()
    {
        if (MainWindow is not MainWindow window)
        {
            _logger.Warning("Tray: the panel window is not available; ignoring the show/hide request");
            return;
        }

        if (window.IsVisible)
        {
            RequestPanelHide("tray");
            return;
        }

        // Showing re-evaluates the monitor and follows the RF-017 policy; never activates
        // (RF-018/RF-024). Fire-and-forget is safe: RequestPanelShowAsync isolates failures.
        _ = RequestPanelShowAsync("tray");
    }

    /// <summary>
    /// Gets the one settings-apply boundary shared by every settings-family editor (the settings
    /// window and the hotkey/appearance editor). It is created once the running configuration, the
    /// single-writer store, and the shared apply pipeline are available, then reused so overlapping
    /// edits from different surfaces are serialized by a single apply gate (M3-RF-615/M2-RF-922).
    /// </summary>
    /// <returns>The shared settings boundary, or <see langword="null"/> before the configuration has loaded.</returns>
    private ISettingsService? GetSharedSettingsService()
    {
        if (_settingsCoordinatorProvider is { } existing)
        {
            return existing.Get();
        }

        var store = _configStore;
        var applier = _panelConfigApplier;
        if (Config is null || store is null || applier is null)
        {
            return null;
        }

        // M2-RF-923/RF-003/RF-004: reuse the M1 per-user startup registration for the
        // start-with-Windows toggle.
        _settingsCoordinatorProvider = new SettingsCoordinatorProvider(
            () => Config,
            store,
            applier,
            () => new RegistryStartupRegistration(new HkcuRunRegistry(), Environment.ProcessPath, _logger),
            updated => Config = updated,
            _logger);

        return _settingsCoordinatorProvider.Get();
    }

    /// <summary>
    /// Opens the tray-accessible settings window (T-17/RF-092/MD-15). This is the one deliberate
    /// activating surface apart from edit mode (M2-RF-924): it is created only in response to an
    /// explicit tray selection and never changes the panel's normal no-activate behavior. The window
    /// is built over the running configuration, the single-writer store, the shared apply pipeline,
    /// the M1 start-with-Windows registration, the existing import/export service, and the existing
    /// monitor enumerator. A failure is isolated and notified (M2-RF-925).
    /// </summary>
    private void OpenSettings()
    {
        try
        {
            if (_settingsWindow is { IsLoaded: true })
            {
                _settingsWindow.Activate();
                return;
            }

            // M3-RF-615/M2-RF-922: every settings-family editor resolves the one shared coordinator
            // so overlapping edits from any surface are serialized by a single apply gate.
            var settings = GetSharedSettingsService();
            if (settings is null)
            {
                _logger.Warning("Settings: the settings window was requested before the configuration was ready");
                Notifier.Notify(
                    "TouchPanel",
                    "The settings window is not available until the configuration has loaded.",
                    NotificationSeverity.Warning);
                return;
            }

            var settingsViewModel = new SettingsViewModel(
                settings,
                () => Config,
                _configImportExport,
                new ConfigFilePicker(),
                ConfigPaths.Default.PanelFile,
                _logger);

            var pickerViewModel = new MonitorPickerViewModel(
                new Win32MonitorEnumerator(_logger),
                settings,
                () => Config?.Monitor,
                Notifier,
                _logger);

            var window = new SettingsWindow(settingsViewModel, pickerViewModel);
            window.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow = window;

            // M2-RF-924: a deliberate activating window; the panel keeps its WS_EX_NOACTIVATE state.
            window.Show();
            window.Activate();

            _logger.Information("Settings: the settings window was opened");
        }
        catch (Exception ex)
        {
            // M2-RF-925/RF-012: a failure building or showing the window must not affect the panel
            // or the tray.
            _logger.Warning(ex, "Settings: opening the settings window failed");
            Notifier.Notify(
                "TouchPanel",
                "The settings window could not be opened.",
                NotificationSeverity.Warning);
        }
    }

    /// <summary>
    /// Opens the focused, tray-accessible hotkey editor (M3/RF-061/M3-RF-611). This is a deliberate
    /// activating surface (the D-6 analogue, M3-RF-616): it is created only in response to an
    /// explicit tray selection and never changes the panel's normal no-activate behavior. The editor
    /// persists through the same single-writer <see cref="SettingsCoordinator"/> path; saving runs the
    /// shared apply pipeline, which re-registers the hotkeys live through
    /// <see cref="IPanelConfigApplyTarget.ApplyHotKeys"/> (M3-RF-614). A failure is isolated and
    /// notified (M3-RF-617/M3-RNF-138).
    /// </summary>
    private void OpenHotkeysEditor()
    {
        try
        {
            if (_hotkeysEditorWindow is { IsLoaded: true })
            {
                _hotkeysEditorWindow.Activate();
                return;
            }

            var config = Config;

            // M3-RF-615/M2-RF-922: reuse the one shared settings coordinator. The apply pipeline
            // invokes ApplyHotKeys, re-registering live without a restart; a failed save never reaches
            // that hook, so the current registrations stay intact. Sharing the coordinator with the
            // settings window means a hotkey/appearance edit and a settings edit can never overlap.
            var settings = GetSharedSettingsService();
            if (config is null || settings is null)
            {
                _logger.Warning("Hotkeys: the editor was requested before the configuration was ready");
                Notifier.Notify(
                    "TouchPanel",
                    "The hotkey editor is not available until the configuration has loaded.",
                    NotificationSeverity.Warning);
                return;
            }

            var model = new HotkeyEditorModel(config.Hotkeys);

            // M3-RF-850: the "Appearance" section shares the same single-writer coordinator; a
            // selection applies live through the apply pipeline's appearance step.
            var appearance = new AppearanceEditorViewModel(
                settings,
                () => Config?.Appearance,
                _logger);

            var viewModel = new HotkeysEditorViewModel(
                model,
                settings,
                Notifier,
                hotkeysAvailable: _hotKeyService is not null,
                reloadSource: () => Config?.Hotkeys ?? [],
                logger: _logger,
                appearance: appearance);

            var window = new HotkeysEditorWindow(viewModel);
            window.Closed += (_, _) => _hotkeysEditorWindow = null;
            _hotkeysEditorWindow = window;

            // M3-RF-616: a deliberate activating window; the panel keeps its WS_EX_NOACTIVATE state.
            window.Show();
            window.Activate();

            Health.MarkRestored("hotkeys-editor", "the hotkey editor was opened");
            _logger.Information("Hotkeys: the hotkey editor was opened");
        }
        catch (Exception ex)
        {
            // M3-RF-617/RF-012: a failure building or showing the window must not affect the panel,
            // the tray, or the currently registered hotkeys.
            _logger.Warning(ex, "Hotkeys: opening the hotkey editor failed");
            Health.MarkDegraded("hotkeys-editor", "opening the hotkey editor failed", ex);
            Notifier.Notify(
                "TouchPanel",
                "The hotkey editor could not be opened.",
                NotificationSeverity.Warning);
        }
    }

    /// <summary>
    /// Handles a show request (tray or second launch, RF-002/RF-024): re-evaluates the target
    /// monitor on demand (no polling, RF-022) off the UI thread, then applies the RF-017 policy on
    /// the UI thread. Fire-and-forget safe; every failure is logged and isolated (RNF-013).
    /// </summary>
    /// <param name="source">Short source tag for logs ("tray", "second-launch").</param>
    private async Task RequestPanelShowAsync(string source)
    {
        try
        {
            var config = Config;
            var selectionService = _monitorSelectionService;
            var viewModel = _mainWindowViewModel;
            if (config is null || selectionService is null || viewModel is null || MainWindow is not MainWindow window)
            {
                _logger.Warning("Panel: a show request arrived before startup completed; ignoring it. Source={Source}", source);
                return;
            }

            if (window.IsVisible)
            {
                return;
            }

            // Monitor enumeration is native but quick; keep it off the UI thread so a pathological
            // driver can never stall the panel or the tray (RNF-004). The continuation resumes on
            // the UI thread captured here, so the window operations below stay on the dispatcher.
            var selection = await Task.Run(() => selectionService.Select(config.Monitor));

            ApplyPanelShowDecision(window, viewModel.RequestShow(selection), source);
        }
        catch (Exception ex)
        {
            // A show request must never surface an exception to the tray or the message pump.
            _logger.Warning(ex, "Panel: handling a show request failed. Source={Source}", source);
        }
    }

    /// <summary>
    /// Hides the panel through the non-activating hide path and updates the view-model state
    /// (RF-006/RF-024).
    /// </summary>
    /// <param name="source">Short source tag for logs ("tray").</param>
    private void RequestPanelHide(string source)
    {
        _mainWindowViewModel?.RequestHide();
        (MainWindow as MainWindow)?.HidePanel();
        _logger.Information("Panel: hidden. Source={Source}", source);
    }

    /// <summary>
    /// Handles the Esc key while edit mode is active (RF-027): exits asynchronously (saving when
    /// dirty) and restores the non-activating normal behavior.
    /// </summary>
    private void OnEditModeExitRequested(object? sender, EventArgs e) => _ = SetEditModeAsync(enter: false);

    /// <summary>
    /// Handles a long press on a widget in normal mode (UX fix #1): enters edit mode asynchronously
    /// through the same deliberate entry path as the tray item. Entry is the only path allowed to
    /// activate the panel (D-6); normal mode never activates.
    /// </summary>
    private void OnEditModeEnterRequested(object? sender, EventArgs e) => _ = SetEditModeAsync(enter: true);

    /// <summary>
    /// Toggles edit mode (RF-026/DD-7). Entering ensures the panel is visible (applying the RF-017
    /// monitor policy), records the DIP page bounds used for clamping, and applies the deliberate
    /// activation allowed by D-6. Exiting flips the flag synchronously, saves the layout only when
    /// dirty through the single-writer store (RF-080), and restores the non-activating show
    /// behavior. The tray check state is kept in sync on both paths; failures are isolated
    /// (RF-013).
    /// </summary>
    /// <param name="enter">Whether edit mode should be entered or exited.</param>
    private async Task SetEditModeAsync(bool enter)
    {
        try
        {
            var service = _editModeService;
            if (service is null || MainWindow is not MainWindow window)
            {
                _logger.Warning("Edit mode: the configuration is not ready; ignoring the request. Enter={Enter}", enter);
                return;
            }

            if (enter)
            {
                if (service.IsEditMode)
                {
                    return;
                }

                if (!window.IsVisible)
                {
                    // Showing follows the same monitor-availability policy as any other show
                    // request (RF-017); edit mode is refused when the panel cannot be shown.
                    await RequestPanelShowAsync("edit-mode");
                }

                if (!window.IsVisible)
                {
                    _logger.Warning("Edit mode: the panel could not be shown (monitor unavailable); staying in normal mode");
                    return;
                }

                // Clamping bounds in DIPs; physical placement stays SetWindowPos-based (DD-5).
                service.Enter(window.ActualWidth, window.ActualHeight);
                window.EnterEditMode();
                _logger.Information(
                    "Edit mode: entered; the panel may take focus for keyboard editing (D-6). PageWidth={PageWidth} PageHeight={PageHeight}",
                    service.PageWidth,
                    service.PageHeight);
            }
            else
            {
                if (!service.IsEditMode && !service.IsDirty)
                {
                    return;
                }

                var result = await service.ExitAsync();
                _logger.Information(
                    "Edit mode: exited. SaveAttempted={SaveAttempted} SaveSucceeded={SaveSucceeded} Canceled={Canceled}",
                    result.Attempted,
                    result.Succeeded,
                    result.Canceled);
            }

            UpdateEditModeMenuItem();
        }
        catch (Exception ex)
        {
            // A tray or keyboard command must never take the application down (RF-013).
            _logger.Warning(ex, "Edit mode: handling the toggle failed");
        }
    }

    /// <summary>
    /// Keeps the tray <b>Edit Mode</b> item's checked state in sync with the service (RF-005).
    /// No-op when the tray is unavailable.
    /// </summary>
    private void UpdateEditModeMenuItem()
    {
        _trayService?.SetMenuItem(
            new TrayMenuItem(
                TrayMenuCommand.EditMode,
                "Edit Mode",
                IsChecked: _editModeService?.IsEditMode == true,
                IsCheckable: true),
            index: 1);
    }

    /// <summary>
    /// RF-006: the panel was closed (Alt+F4 or a system close request) and hid itself to the tray.
    /// Marks the view model hidden and shows the one-time notification explaining that TouchPanel
    /// keeps running and how to exit; the flag is persisted so the notification appears exactly
    /// once across restarts.
    /// </summary>
    private void OnCloseToTrayRequested(object? sender, EventArgs e)
    {
        _mainWindowViewModel?.RequestHide();
        _logger.Information("Panel: close request handled; the panel was hidden to the tray and the application keeps running");

        if (_mainWindowViewModel?.TryConsumeCloseNotification() != true)
        {
            return;
        }

        Notifier.Notify(
            "TouchPanel",
            "TouchPanel keeps running in the tray. Use the tray menu to show the panel again or to exit.",
            NotificationSeverity.Info);

        _ = SaveCloseNotificationStateAsync();
    }

    /// <summary>
    /// Persists the consumed close-notification flag through <see cref="StateStore"/> (RF-006,
    /// RF-073). Runs off the UI thread; failures are logged and non-fatal (the notification may
    /// then be shown again after a restart).
    /// </summary>
    private async Task SaveCloseNotificationStateAsync()
    {
        var stateStore = _stateStore;
        var state = _runtimeState;
        if (stateStore is null || state is null)
        {
            return;
        }

        try
        {
            var saved = await Task.Run(() => stateStore.SaveAsync(state).AsTask()).ConfigureAwait(false);
            if (!saved)
            {
                _logger.Warning("Runtime state: the close-notification flag could not be persisted");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Runtime state: persisting the close-notification flag failed unexpectedly");
        }
    }

    /// <summary>
    /// Opens the resolved configuration directory in Explorer (RF-005). Directory creation and
    /// the process launch run off the UI thread (RNF-004); failures notify and log, never throw.
    /// </summary>
    private async Task OpenConfigurationFolderAsync()
    {
        var directory = Paths.DataDirectory;

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = false,
                    ArgumentList = { directory },
                });
            });

            _logger.Information("Tray: opened the configuration folder. Directory={Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Tray: opening the configuration folder failed. Directory={Directory}", directory);
            Notifier.Notify("TouchPanel", "The configuration folder could not be opened.", NotificationSeverity.Warning);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        // RF-007: stop subsystems within the ~3 s budget before the final log entries.
        RunShutdownCoordinator();
        AppLog.Shutdown(_logger);
        FlushLogs();

        base.OnExit(e);
    }

    private void OnShowPanelRequested(object? sender, EventArgs e)
    {
        // RF-002/RF-017/RF-018: a second launch asks for the panel; showing follows the same
        // non-activating, monitor-availability policy as the tray request.
        _logger.Information("Startup: show-panel request received from a second launch");
        _ = RequestPanelShowAsync("second-launch");
    }

    /// <summary>
    /// Handles an RF-006 exit request from a later <c>TouchPanel.exe --exit</c> launch: runs the
    /// same clean shutdown as the tray Exit command, without activating or showing any window.
    /// The request arrives on the signal window's thread (the UI thread); marshalling defensively
    /// keeps the shutdown on the dispatcher.
    /// </summary>
    private void OnExitRequested(object? sender, EventArgs e)
    {
        _logger.Information("Startup: exit request received from a later launch");

        if (Dispatcher.CheckAccess())
        {
            RequestApplicationExit("exit-request");
            return;
        }

        Dispatcher.BeginInvoke(() => RequestApplicationExit("exit-request"));
    }

    /// <summary>
    /// Performs the explicit shutdown path (tray Exit or a later <c>--exit</c> launch): allow the
    /// close-to-tray-intercepted panel window to close, then let WPF shut down so the ordered
    /// lifecycle coordinator runs (RF-006/RF-007). Never activates or shows a window.
    /// </summary>
    /// <param name="source">Short source tag for logs ("tray", "exit-request").</param>
    private void RequestApplicationExit(string source)
    {
        _logger.Information("Shutdown: exit requested; shutting down. Source={Source}", source);

        // T-08/RF-006/RF-007: the close-to-tray interception must not block the real shutdown;
        // allow the panel window to close before WPF starts closing windows.
        if (MainWindow is MainWindow panel)
        {
            panel.AllowExit = true;
        }

        Shutdown();
    }

    /// <summary>
    /// Runs the ordered shutdown (RF-007). The coordinator is bounded by its budget and isolates
    /// participant failures, so this never blocks the exit indefinitely.
    /// </summary>
    private void RunShutdownCoordinator()
    {
        var lifecycle = _lifecycle;
        if (lifecycle is null)
        {
            // Defensive path: exit before composition completed; release what exists directly.
            try
            {
                _singleInstance?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Shutdown: disposing the single-instance service failed");
            }

            return;
        }

        try
        {
            lifecycle.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // ShutdownAsync never faults by design; this guards against coordinator bugs.
            _logger.Warning(ex, "Shutdown: the lifecycle coordinator failed; continuing with log flush");
        }
    }

    /// <summary>
    /// F-08/RF-077: a dirty layout could not be saved during shutdown (for example because the
    /// file was locked or the disk was full). Writes a best-effort <c>panel.recovery.json</c>
    /// through the configuration store, without touching <c>panel.json</c> or the backup. The
    /// store never throws, and the attempt is bounded by the shared shutdown budget.
    /// </summary>
    /// <param name="cancellationToken">The shutdown budget token.</param>
    private async ValueTask TryWriteEmergencyRecoveryCopyAsync(CancellationToken cancellationToken)
    {
        var store = _configStore;
        var config = Config;
        if (store is null || config is null)
        {
            _logger.Warning("Shutdown: no configuration store or configuration is available for the recovery copy");
            return;
        }

        var written = await store.TryWriteRecoveryCopyAsync(config, cancellationToken).ConfigureAwait(false);
        if (written)
        {
            _logger.Warning("Shutdown: the layout save failed; a recovery copy was written instead");
        }
        else
        {
            _logger.Warning("Shutdown: the layout save failed and no recovery copy could be written");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ExceptionLog.DispatcherException(_logger, e.Exception);
        FlushLogs();

        // e.Handled intentionally stays false: continuing after an unknown UI-thread
        // failure is not provably safe; RF-011 requires the log entry before termination.
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        ExceptionLog.AppDomainException(_logger, e.ExceptionObject as Exception, e.IsTerminating);
        FlushLogs();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ExceptionLog.UnobservedTaskException(_logger, e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Flushes and closes the global logger from a fatal or shutdown path. Idempotent and
    /// never throws; after the flush the field logger is switched to Serilog's silent
    /// logger so later handlers (for example <see cref="OnExit"/>) cannot hit a disposed sink.
    /// </summary>
    private void FlushLogs()
    {
        try
        {
            Log.CloseAndFlush();
            _logger = Serilog.Log.Logger;
        }
        catch (Exception ex)
        {
            // The last-resort diagnostics path must never make a fatal situation worse.
            Trace.WriteLine($"TouchPanel: log flush failed ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>
    /// Background-resolved data the UI thread needs to apply the startup panel state (T-08):
    /// the effective configuration, the RF-017 monitor selection result, and the persisted
    /// runtime flags.
    /// </summary>
    /// <param name="Config">The effective configuration.</param>
    /// <param name="Selection">The resolved target monitor (or the unavailable/fallback result).</param>
    /// <param name="RuntimeState">The loaded <c>state.json</c> flags.</param>
    private sealed record PanelStartupContext(
        PanelConfig Config,
        MonitorSelectionResult Selection,
        RuntimeState RuntimeState);
}
