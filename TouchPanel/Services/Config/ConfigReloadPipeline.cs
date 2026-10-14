using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Health;
using TouchPanel.Services.Notifications;

namespace TouchPanel.Services.Config;

/// <summary>
/// Event source for reload requests (MD-9). The production implementation wraps the debounced
/// <see cref="System.IO.FileSystemWatcher"/>; the abstraction lets the apply pipeline be exercised
/// deterministically without touching the file system (M2-RNF-124).
/// </summary>
public interface IConfigReloadSource : IDisposable
{
    /// <summary>Raised on the debounce thread when an external change must be read and applied.</summary>
    event EventHandler<ConfigReloadRequestedEventArgs>? ReloadRequested;

    /// <summary>Starts watching (idempotent).</summary>
    void Start();
}

/// <summary>
/// The hot-reload apply flow (M2-RF-822/M2-RF-824/M2-RF-826/M2-RF-829, MD-9): it subscribes to the
/// reload controller, reads the effective file as a whole, and only applies a fully valid candidate
/// through the shared <see cref="PanelConfigApplier"/>; on any parse/migration/validation/read
/// failure the currently running configuration stays active, the failure is logged and (throttled)
/// notified, and nothing is partially applied. Reload work is serialized so overlapping watcher
/// events cannot interleave, and all failures are isolated via <see cref="SubsystemHealth"/>.
/// </summary>
public sealed class ConfigReloadPipeline : IDisposable
{
    /// <summary>Default window between reload-failure notifications so a repeatedly edited invalid file cannot spam balloons (M2-RF-829).</summary>
    public static readonly TimeSpan DefaultFailureNotificationInterval = TimeSpan.FromMinutes(1);

    private readonly IConfigReloadSource _source;
    private readonly ReloadDecisionEngine _engine;
    private readonly IPanelConfigCandidateReader _reader;
    private readonly PanelConfigApplier _applier;
    private readonly Func<PanelConfig?> _configAccessor;
    private readonly Action<PanelConfig> _configSetter;
    private readonly IUserNotifier _notifier;
    private readonly SubsystemHealth? _health;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _failureNotificationInterval;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _lastFailureNotification = DateTimeOffset.MinValue;
    private bool _subscribed;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigReloadPipeline"/> class.
    /// </summary>
    /// <param name="source">The reload-request source (production: <c>ConfigReloadController</c>).</param>
    /// <param name="engine">The pure decision component, consulted for the content-unchanged no-op and updated on success.</param>
    /// <param name="reader">Reads/migrates/validates the external candidate.</param>
    /// <param name="applier">The shared apply pipeline.</param>
    /// <param name="configAccessor">Returns the currently running configuration; used to keep the failure path a no-op when nothing is loaded yet.</param>
    /// <param name="configSetter">Atomically publishes the newly applied configuration as the running one.</param>
    /// <param name="notifier">The user-notification boundary (RF-010/M2-RF-826/M2-RF-829).</param>
    /// <param name="timeProvider">Clock used for the failure-notification throttle; defaults to the system clock.</param>
    /// <param name="health">Optional degraded-subsystem registry (RF-012).</param>
    /// <param name="failureNotificationInterval">Throttle window for failure notifications; defaults to one minute.</param>
    /// <param name="logger">Logger; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public ConfigReloadPipeline(
        IConfigReloadSource source,
        ReloadDecisionEngine engine,
        IPanelConfigCandidateReader reader,
        PanelConfigApplier applier,
        Func<PanelConfig?> configAccessor,
        Action<PanelConfig> configSetter,
        IUserNotifier notifier,
        TimeProvider? timeProvider = null,
        SubsystemHealth? health = null,
        TimeSpan? failureNotificationInterval = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(configAccessor);
        ArgumentNullException.ThrowIfNull(configSetter);
        ArgumentNullException.ThrowIfNull(notifier);

        _source = source;
        _engine = engine;
        _reader = reader;
        _applier = applier;
        _configAccessor = configAccessor;
        _configSetter = configSetter;
        _notifier = notifier;
        _health = health;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _failureNotificationInterval = failureNotificationInterval ?? DefaultFailureNotificationInterval;
        _logger = (logger ?? Log.Logger).ForContext<ConfigReloadPipeline>();
    }

    /// <summary>Gets the number of reloads that successfully applied a valid candidate (for diagnostics/tests).</summary>
    public int AppliedCount { get; private set; }

    /// <summary>Gets the number of reload attempts rejected because the candidate was not valid (for diagnostics/tests).</summary>
    public int RejectedCount { get; private set; }

    /// <summary>
    /// Subscribes the apply flow and starts the underlying watcher. Idempotent; a watcher that
    /// cannot be established degrades gracefully inside the controller (M2-RF-827).
    /// </summary>
    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        _source.ReloadRequested += OnReloadRequested;
        _subscribed = true;
        _source.Start();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_subscribed)
        {
            _source.ReloadRequested -= OnReloadRequested;
            _subscribed = false;
        }

        _source.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnReloadRequested(object? sender, ConfigReloadRequestedEventArgs e)
    {
        // Fire-and-forget from the debounce thread; every failure is contained below so the
        // watcher and the application keep running (M2-RF-827).
        _ = HandleReloadAsync(e);
    }

    private async Task HandleReloadAsync(ConfigReloadRequestedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // Serialize reloads: a burst of watcher events must not interleave two applies.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            if (_configAccessor() is null)
            {
                // No effective configuration is loaded yet; the startup path owns the first apply,
                // so a reload must not race it (M2-RF-822).
                _logger.Debug("Hot reload: no running configuration is loaded yet; the reload request was ignored");
                return;
            }

            var candidate = await _reader.ReadAsync().ConfigureAwait(false);
            if (!candidate.IsValid)
            {
                ReportRejected(candidate);
                return;
            }

            // The content-unchanged no-op (M2-RF-823): an event for byte-identical content does no work.
            if (candidate.RawContent is { } raw
                && _engine.EvaluateCandidate(raw) == ReloadDecision.Ignore)
            {
                _logger.Debug("Hot reload: the candidate is identical to the applied content; nothing to do");
                return;
            }

            // Atomic in-memory swap, then the shared apply pipeline. Validation guarantees the
            // candidate is coherent; step failures are isolated by the applier.
            _configSetter(candidate.Config!);
            var failures = _applier.Apply(candidate.Config!);

            if (candidate.RawContent is { } appliedRaw)
            {
                // Record the applied bytes so a later event with the same content is a no-op and
                // so the running content is the reference for the next comparison (M2-RF-823).
                _engine.RecordAppliedContent(appliedRaw);
            }

            AppliedCount++;
            _health?.MarkRestored("configuration", "an external configuration was applied");

            _logger.Information(
                "Hot reload: applied an external configuration. DetectedSchemaVersion={SchemaVersion} StepFailures={StepFailures} Path={Path}",
                candidate.DetectedSchemaVersion,
                failures,
                e.PanelFilePath);
        }
        catch (OperationCanceledException)
        {
            // Shutdown canceled the read; nothing was applied.
        }
        catch (Exception ex)
        {
            // Defensive: the reader/applier already isolate operationally, but a composition bug
            // must never take down the watcher thread or the application (M2-RF-827).
            _logger.Error(ex, "Hot reload: applying an external configuration failed unexpectedly; the running configuration is kept");
            _health?.MarkDegraded("configuration", "applying an external configuration failed", ex);
            NotifyFailure("The external configuration edit could not be applied; TouchPanel keeps running with the current configuration.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ReportRejected(PanelConfigCandidate candidate)
    {
        RejectedCount++;

        foreach (var diagnostic in candidate.Diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case ConfigDiagnosticSeverity.Error:
                    _logger.Error("Hot reload rejected the external configuration {Code}: {Message}", diagnostic.Code, diagnostic.Message);
                    break;
                case ConfigDiagnosticSeverity.Warning:
                    _logger.Warning("Hot reload diagnostic {Code}: {Message}", diagnostic.Code, diagnostic.Message);
                    break;
                default:
                    _logger.Information("Hot reload diagnostic {Code}: {Message}", diagnostic.Code, diagnostic.Message);
                    break;
            }
        }

        if (candidate.Outcome == PanelConfigCandidateOutcome.Missing)
        {
            // A deleted file is not an error the user must act on; the running configuration stays.
            _health?.MarkDegraded("configuration", "the configuration file is missing; the running configuration is kept");
            return;
        }

        _health?.MarkDegraded("configuration", "the external configuration was rejected; the running configuration is kept");

        var message = candidate.Outcome == PanelConfigCandidateOutcome.UnsupportedFuture
            ? "The edited configuration was created by a newer version of TouchPanel and was not applied; the running configuration is kept."
            : "The edited configuration is not valid and was not applied; the running configuration is kept.";

        NotifyFailure(message);
    }

    private void NotifyFailure(string message)
    {
        var now = _timeProvider.GetUtcNow();
        if (now - _lastFailureNotification < _failureNotificationInterval)
        {
            _logger.Debug("Hot reload: suppressing a repeated failure notification within the throttle window");
            return;
        }

        _lastFailureNotification = now;
        _notifier.Notify("TouchPanel", message, NotificationSeverity.Warning);
    }
}
