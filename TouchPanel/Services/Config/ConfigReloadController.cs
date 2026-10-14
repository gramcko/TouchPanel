using System.IO;
using Serilog;
using TouchPanel.Config;

namespace TouchPanel.Services.Config;

/// <summary>
/// Raised after a debounced, non-suppressed change to the effective configuration file. The apply
/// pipeline (part 2) subscribes, reads the file, validates it as a whole, and swaps it in
/// (M2-RF-822/M2-RF-824). The event is raised on the debounce timer's thread, so subscribers that
/// touch UI state must marshal to the UI thread themselves.
/// </summary>
public sealed class ConfigReloadRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigReloadRequestedEventArgs"/> class.
    /// </summary>
    /// <param name="panelFilePath">The effective configuration file whose change triggered the request.</param>
    public ConfigReloadRequestedEventArgs(string panelFilePath) => PanelFilePath = panelFilePath;

    /// <summary>Gets the full path of the effective configuration file (<c>panel.json</c>).</summary>
    public string PanelFilePath { get; }
}

/// <summary>
/// Thin <see cref="FileSystemWatcher"/> adapter for hot reload (M2-RF-821..M2-RF-830, MD-9). The
/// watcher is scoped to the config directory with <c>Filter = "panel.json"</c> and
/// <c>NotifyFilter = LastWrite | FileName | Size | CreationTime</c>, so writes to
/// <c>panel.backup.json</c>, <c>state.json</c>, <c>panel.recovery.json</c>,
/// <c>panel.corrupt-*.json</c>, and the atomic-save temp file produce no event at all. Those raw
/// events are normalized into <see cref="ConfigFileChange"/> and fed to the pure
/// <see cref="ReloadDecisionEngine"/>, which coalesces them (debounce) and suppresses the
/// application's own writes. The controller performs no polling, never writes the watched file,
/// isolates watcher failures, and disposes the watcher and any pending debounce timer cleanly
/// (M2-RF-827/M2-RF-828).
/// </summary>
public sealed class ConfigReloadController : IConfigReloadSource
{
    private readonly ReloadDecisionEngine _engine;
    private readonly ConfigPaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private FileSystemWatcher? _watcher;
    private ITimer? _debounceTimer;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigReloadController"/> class.
    /// </summary>
    /// <param name="engine">The pure decision component that owns the suppression/debounce semantics.</param>
    /// <param name="paths">Injected file paths; defaults to the real per-user profile (tests inject a temp directory).</param>
    /// <param name="timeProvider">Clock/timer source for the debounce; defaults to the system clock.</param>
    /// <param name="logger">Logger for watcher lifecycle and resilience messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="engine"/> is <see langword="null"/>.</exception>
    public ConfigReloadController(
        ReloadDecisionEngine engine,
        ConfigPaths? paths = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(engine);

        _engine = engine;
        _paths = paths ?? ConfigPaths.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (logger ?? Log.Logger).ForContext<ConfigReloadController>();
    }

    /// <summary>
    /// Raised on the debounce thread when an external change must be read and applied. Subscribers
    /// perform validate-then-apply (part 2); the controller itself never reads the file.
    /// </summary>
    public event EventHandler<ConfigReloadRequestedEventArgs>? ReloadRequested;

    /// <summary>Gets a value indicating whether the watcher is currently active.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _started && !_disposed && _watcher is not null;
            }
        }
    }

    /// <summary>
    /// Establishes the watcher if it is not already running. Idempotent; a directory or watcher
    /// failure is logged and leaves hot reload degraded without throwing (M2-RF-827). A later call
    /// retries when the previous attempt could not establish the watcher.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _started = true;
            if (_watcher is not null)
            {
                return;
            }
        }

        TryStartWatcher();
    }

    /// <summary>
    /// Stops watching and releases the debounce timer. Idempotent; the instance can be
    /// <see cref="Start"/>ed again afterwards.
    /// </summary>
    public void Stop()
    {
        FileSystemWatcher? watcher;
        ITimer? timer;
        lock (_gate)
        {
            _started = false;
            watcher = _watcher;
            _watcher = null;
            timer = _debounceTimer;
            _debounceTimer = null;
        }

        Detach(watcher);
        timer?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        GC.SuppressFinalize(this);
    }

    private void TryStartWatcher()
    {
        FileSystemWatcher? watcher = null;
        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);

            watcher = new FileSystemWatcher(_paths.DataDirectory)
            {
                // M2-RF-821: only the effective configuration file. A literal file name matches
                // exactly, so panel.backup.json / panel.json.tmp / state.json never raise events.
                Filter = ConfigPaths.PanelFileName,
                NotifyFilter = NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false,
            };

            watcher.Changed += OnConfigFileChanged;
            watcher.Created += OnConfigFileCreated;
            watcher.Deleted += OnConfigFileDeleted;
            watcher.Renamed += OnConfigFileRenamed;
            watcher.Error += OnWatcherError;

            lock (_gate)
            {
                if (_disposed || !_started)
                {
                    Detach(watcher);
                    return;
                }

                _watcher = watcher;
            }

            watcher.EnableRaisingEvents = true;
            _logger.Information(
                "Hot reload: watching {Directory} for {Filter}; notify filter is LastWrite|FileName|Size|CreationTime",
                _paths.DataDirectory,
                ConfigPaths.PanelFileName);
        }
        catch (Exception ex)
        {
            Detach(watcher);
            _logger.Warning(
                ex,
                "Hot reload: the configuration watcher could not be established; hot reload is degraded until it can be re-established");
        }
    }

    private void HandleFileSignal(string? path, ConfigFileChangeKind kind)
    {
        ReloadDecision decision;
        try
        {
            decision = _engine.NotifyChange(new ConfigFileChange(kind, path, _timeProvider.GetUtcNow()));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hot reload: evaluating a file-system event failed");
            return;
        }

        if (decision == ReloadDecision.Debounce)
        {
            ArmDebounce();
        }
    }

    private void ArmDebounce()
    {
        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }

            if (_debounceTimer is null)
            {
                _debounceTimer = _timeProvider.CreateTimer(
                    _ => OnDebounceElapsed(),
                    state: null,
                    dueTime: _engine.DebounceWindow,
                    period: Timeout.InfiniteTimeSpan);
            }
            else
            {
                _debounceTimer.Change(_engine.DebounceWindow, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnDebounceElapsed()
    {
        ReloadDecision decision;
        try
        {
            decision = _engine.NotifyDebounceElapsed();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hot reload: evaluating the debounce failed");
            return;
        }

        if (decision == ReloadDecision.Debounce)
        {
            ArmDebounce();
            return;
        }

        if (decision != ReloadDecision.Reload)
        {
            return;
        }

        try
        {
            ReloadRequested?.Invoke(this, new ConfigReloadRequestedEventArgs(_paths.PanelFile));
        }
        catch (Exception ex)
        {
            // A subscriber failure must not take down the watcher or the application (M2-RF-827).
            _logger.Error(ex, "Hot reload: a reload-requested handler failed");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e) =>
        HandleFileSignal(e.FullPath, ConfigFileChangeKind.Changed);

    private void OnConfigFileCreated(object sender, FileSystemEventArgs e) =>
        HandleFileSignal(e.FullPath, ConfigFileChangeKind.Created);

    private void OnConfigFileDeleted(object sender, FileSystemEventArgs e) =>
        HandleFileSignal(e.FullPath, ConfigFileChangeKind.Deleted);

    private void OnConfigFileRenamed(object sender, RenamedEventArgs e) =>
        HandleFileSignal(e.FullPath, ConfigFileChangeKind.Renamed);

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.Warning(
            e.GetException(),
            "Hot reload: the configuration watcher reported an error; attempting to re-establish it");

        FileSystemWatcher? old;
        lock (_gate)
        {
            old = _watcher;
            _watcher = null;
        }

        Detach(old);

        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }
        }

        TryStartWatcher();
    }

    private void Detach(FileSystemWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (Exception)
        {
            // Best effort; Dispose still releases the handle.
        }

        watcher.Changed -= OnConfigFileChanged;
        watcher.Created -= OnConfigFileCreated;
        watcher.Deleted -= OnConfigFileDeleted;
        watcher.Renamed -= OnConfigFileRenamed;
        watcher.Error -= OnWatcherError;
        watcher.Dispose();
    }
}
