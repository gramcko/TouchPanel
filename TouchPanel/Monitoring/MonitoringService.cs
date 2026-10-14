using Serilog;
using TouchPanel.Services.Health;

namespace TouchPanel.Monitoring;

/// <summary>
/// The production monitoring service (DD-10, RF-062..RF-068): a <see cref="PeriodicTimer"/> loop on
/// a background task polls the injected <see cref="ISensorSource"/> at a configurable interval
/// (RF-071, 0.5–5 s, default 1 s), publishes immutable snapshots through
/// <see cref="SnapshotAvailable"/>, contains every source and subscriber failure with throttled
/// logging (RF-065), and stops/disposes cleanly so no timer or background task survives shutdown
/// (RF-066). The interval can be changed live via <see cref="ApplyInterval"/> without leaving two
/// loops running or leaking the previous timer (M2-RF-712). When an optional
/// <see cref="BackendReinitializer"/> is supplied, a backend failure is retried on a bounded
/// throttle and the <c>monitoring</c> subsystem is restored on recovery (RF-070/M2-RF-701..704).
/// </summary>
/// <remarks>
/// <para><b>Threading (RF-067).</b> The loop, the hardware poll, and snapshot publication all run
/// on a thread-pool task; the UI thread is never touched. Subscribers that update view models
/// marshal to their dispatcher themselves.</para>
/// <para><b>Failure isolation (RF-012/RNF-013).</b> A failing source yields a fully unavailable
/// snapshot and the loop keeps polling; a throwing subscriber does not starve the others.</para>
/// </remarks>
public sealed class MonitoringService : IMonitoringService
{
    /// <summary>The default M1 polling interval: approximately 1 Hz (RF-064/RF-071).</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Lowest accepted polling interval, 0.5 s (RF-071/M2-RF-711). Mirrors
    /// <c>TelemetryConfig.MinPollIntervalSeconds</c>; <see cref="ApplyInterval"/> clamps to this
    /// defensively so a value that skipped configuration validation can never drive a runaway rate.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// Highest accepted polling interval, 5 s (RF-071/M2-RF-711). Mirrors
    /// <c>TelemetryConfig.MaxPollIntervalSeconds</c>; <see cref="ApplyInterval"/> clamps to this
    /// defensively.
    /// </summary>
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long <see cref="Stop"/> waits for the loop task. Hardware reads are short and
    /// cancellation is observed between ticks; a pathological read must not hang shutdown (RF-007).
    /// </summary>
    public static readonly TimeSpan DefaultLoopStopTimeout = TimeSpan.FromSeconds(2);

    private readonly ISensorSource _source;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _loopStopTimeout;
    private readonly ThrottledFailureLogger _sourceFailureLog;
    private readonly ThrottledFailureLogger _subscriberFailureLog;
    private readonly SensorHistoryStore? _history;
    private readonly SubsystemHealth? _health;
    private readonly BackendReinitializer? _backendReinitializer;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly object _applyGate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private TimeSpan _interval;
    private SensorSnapshot? _latest;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitoringService"/> class.
    /// </summary>
    /// <param name="source">The hardware sensor source; owned and disposed by this service.</param>
    /// <param name="timeProvider">Clock for the cadence and failure throttling; defaults to the system clock.</param>
    /// <param name="interval">The polling interval; defaults to <see cref="DefaultInterval"/>.</param>
    /// <param name="failureLogThrottle">The failure-log throttle window; defaults to one minute.</param>
    /// <param name="loopStopTimeout">The bounded wait for the loop task during <see cref="Stop"/>.</param>
    /// <param name="logger">Logger for lifecycle and failure entries; defaults to the global Serilog logger.</param>
    /// <param name="health">Optional degraded-subsystem registry; a failing backend is marked <c>monitoring</c> degraded and restored on recovery (RF-012).</param>
    /// <param name="history">Optional bounded-history store fed from every produced snapshot (RF-069/MD-5); owned by the composition root, not disposed here.</param>
    /// <param name="backendReinitializer">Optional throttled backend re-initializer (RF-070/MD-7); when supplied, a reported backend failure is retried on its bounded cadence and the subsystem is restored on success. Omitted for sources that cannot recover.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    internal MonitoringService(
        ISensorSource source,
        TimeProvider? timeProvider = null,
        TimeSpan? interval = null,
        TimeSpan? failureLogThrottle = null,
        TimeSpan? loopStopTimeout = null,
        ILogger? logger = null,
        SubsystemHealth? health = null,
        SensorHistoryStore? history = null,
        BackendReinitializer? backendReinitializer = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interval = interval ?? DefaultInterval;
        _loopStopTimeout = loopStopTimeout ?? DefaultLoopStopTimeout;
        _logger = logger ?? Log.Logger;
        _health = health;
        _history = history;
        _backendReinitializer = backendReinitializer;

        // Separate throttles so a stream of source failures cannot hide a subscriber failure
        // (and vice versa); each gets at least its first log entry within the window.
        _sourceFailureLog = new ThrottledFailureLogger(_logger, _timeProvider, failureLogThrottle);
        _subscriberFailureLog = new ThrottledFailureLogger(_logger, _timeProvider, failureLogThrottle);

        if (_interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), _interval, "The polling interval must be positive.");
        }

        if (_loopStopTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(loopStopTimeout), _loopStopTimeout, "The stop timeout must not be negative.");
        }
    }

    /// <inheritdoc />
    public event Action<SensorSnapshot>? SnapshotAvailable;

    /// <summary>Gets the configured polling interval.</summary>
    public TimeSpan Interval
    {
        get
        {
            lock (_gate)
            {
                return _interval;
            }
        }
    }

    /// <inheritdoc />
    public SensorSnapshot? Latest => Volatile.Read(ref _latest);

    /// <inheritdoc />
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _loop is not null;
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">Thrown when the service was already disposed.</exception>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_loop is not null)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = Task.Run(() => RunAsync(cts.Token));
        }

        _logger.Information(
            "Monitoring: telemetry polling started. IntervalMs={IntervalMs}",
            _interval.TotalMilliseconds);
    }

    /// <summary>
    /// Applies a new polling interval live (RF-071/M2-RF-711..714, plan MD-6). The value is clamped
    /// to <see cref="MinimumInterval"/>–<see cref="MaximumInterval"/> with a warning; the bounded
    /// history capacity is recomputed for the same window preserving the newest samples
    /// (M2-RF-695); and, if polling is running, the single loop is restarted at the new cadence so
    /// the previous timer is disposed before the next is armed (no duplicate loop, M2-RF-712). A
    /// restart publishes at most one immediate sample. Safe to call while stopped (the new interval
    /// is used on the next <see cref="Start"/>). This is the apply entry point the configuration
    /// apply/hot-reload pipeline (T-11) calls.
    /// </summary>
    /// <param name="interval">The requested interval; clamped when outside the accepted range.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the service was already disposed.</exception>
    public void ApplyInterval(TimeSpan interval)
    {
        var clamped = ClampInterval(interval);

        lock (_applyGate)
        {
            bool restart;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var changed = clamped != _interval;
                _interval = clamped;
                restart = changed && _loop is not null;
            }

            if (clamped != interval)
            {
                _logger.Warning(
                    "Monitoring: the requested polling interval {RequestedMs} ms was clamped to {ClampedMs} ms (0.5–5 s, RF-071)",
                    interval.TotalMilliseconds,
                    clamped.TotalMilliseconds);
            }

            // Recompute the ring capacity before restarted polling records new samples so the new
            // cadence and the retained history stay consistent (M2-RF-695). Reconfiguration is a
            // no-op when the capacity is unchanged.
            _history?.Reconfigure(clamped);

            if (restart)
            {
                Stop();
                Start();
                _logger.Information(
                    "Monitoring: the polling interval was applied live. IntervalMs={IntervalMs}",
                    clamped.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// Clamps a requested interval into <see cref="MinimumInterval"/>–<see cref="MaximumInterval"/>
    /// (RF-071/M2-RF-711). Non-positive values clamp to the minimum rather than throwing so a
    /// malformed apply request can never disable or accelerate monitoring.
    /// </summary>
    /// <param name="interval">The requested interval.</param>
    /// <returns>The interval bounded to the accepted range.</returns>
    public static TimeSpan ClampInterval(TimeSpan interval)
    {
        if (interval < MinimumInterval)
        {
            return MinimumInterval;
        }

        return interval > MaximumInterval ? MaximumInterval : interval;
    }

    /// <inheritdoc />
    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            if (_cts is null && _loop is null)
            {
                return;
            }

            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already cancelled by a concurrent stop; nothing to do.
        }

        if (loop is not null)
        {
            try
            {
                if (!loop.Wait(_loopStopTimeout))
                {
                    _logger.Warning(
                        "Monitoring: the polling loop did not stop within {TimeoutMs} ms; it will exit at its next cancellation check",
                        _loopStopTimeout.TotalMilliseconds);
                }
            }
            catch (AggregateException ex)
            {
                _sourceFailureLog.Report(ex.Flatten(), "Monitoring: the polling loop ended with an unexpected failure");
            }
        }

        cts?.Dispose();
        _logger.Information("Monitoring: telemetry polling stopped");
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

        // RF-066: close the hardware library on the shutdown path.
        if (_source is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Monitoring: disposing the sensor source failed");
            }
        }
    }

    /// <summary>
    /// Runs one poll-and-publish cycle. Exposed internally so tests can drive the projection,
    /// failure, and subscriber-isolation behavior deterministically without the timer.
    /// </summary>
    internal void PublishOnce()
    {
        SensorSnapshot snapshot;
        try
        {
            // RF-065: the adapter contract is never-throw, but a defective source must still not
            // take the loop down; failures degrade to an explicit all-unavailable snapshot.
            snapshot = _source.Poll() ?? SensorSnapshot.CreateUnavailable(_timeProvider.GetUtcNow());
            UpdateHealthAfterPoll();
        }
        catch (Exception ex)
        {
            _sourceFailureLog.Report(ex, "Monitoring: reading hardware sensors failed");
            _health?.MarkDegraded("monitoring", "reading hardware sensors failed", ex);

            // RF-070/MD-7: a source that can re-open its backend is asked to, on the same bounded
            // throttle, even when the failure surfaced as a throw.
            TryRecoverBackend();
            snapshot = SensorSnapshot.CreateUnavailable(_timeProvider.GetUtcNow());
        }

        Volatile.Write(ref _latest, snapshot);

        // RF-069/MD-5: feed the bounded history from the snapshot this cycle already produced;
        // this is not a second polling path. A history failure is isolated so it can never stop
        // the loop (RF-012).
        try
        {
            _history?.Record(snapshot);
        }
        catch (Exception ex)
        {
            _sourceFailureLog.Report(ex, "Monitoring: recording telemetry history failed");
        }

        PublishToSubscribers(snapshot);
    }

    /// <summary>
    /// Marks the <c>monitoring</c> subsystem degraded or restored from the source's own failure
    /// signal (RF-012): a source that distinguishes a backend failure from legitimately missing
    /// sensors reports it through <see cref="ISensorSourceHealth"/>. Transitions are logged once
    /// per state change by <see cref="SubsystemHealth"/>, so repeated failures stay degraded
    /// without log spam (RF-065/RNF-007).
    /// </summary>
    private void UpdateHealthAfterPoll()
    {
        var health = _health;

        if (_source is ISensorSourceHealth sourceHealth && sourceHealth.LastFailureReason is { } reason)
        {
            health?.MarkDegraded("monitoring", reason);

            // RF-070/M2-RF-701..704: retry a failed backend on the bounded cadence supplied by the
            // re-initializer. A success restores the subsystem immediately (M2-RF-702); a throttled
            // or failed attempt leaves it degraded and the loop keeps publishing unavailable
            // snapshots (M2-RF-703).
            TryRecoverBackend();
            return;
        }

        // The backend is reachable; re-anchor the retry window so a later failure is not retried
        // back-to-back (M2-RF-701).
        _backendReinitializer?.OnBackendHealthy();
        health?.MarkRestored("monitoring", "the hardware backend is reachable");
    }

    /// <summary>
    /// Attempts a throttled backend re-initialization and restores the <c>monitoring</c> subsystem
    /// when it succeeds (RF-070/M2-RF-702). A no-op when the composition root did not supply a
    /// <see cref="BackendReinitializer"/>. Runs on the polling thread; the attempt never blocks the
    /// UI thread and never requests elevation (M2-RF-703/RF-068).
    /// </summary>
    private void TryRecoverBackend()
    {
        if (_backendReinitializer is { } reinitializer && reinitializer.TryRecover())
        {
            _health?.MarkRestored("monitoring", "the hardware backend re-initialized successfully");
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval, _timeProvider);
        try
        {
            // First sample immediately so gauges show data without waiting a full interval;
            // subsequent samples follow the PeriodicTimer cadence exactly (RF-064).
            PublishOnce();

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                PublishOnce();
            }
        }
        catch (OperationCanceledException)
        {
            // The normal stop/dispose path; no further polling happens once this returns.
        }
        catch (Exception ex)
        {
            // Defensive: the loop must never die silently (RF-012).
            _sourceFailureLog.Report(ex, "Monitoring: the polling loop stopped unexpectedly");
        }
    }

    private void PublishToSubscribers(SensorSnapshot snapshot)
    {
        var handlers = SnapshotAvailable;
        if (handlers is null)
        {
            return;
        }

        // Isolate each subscriber so one failing widget cannot starve the others (RF-012).
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<SensorSnapshot>)handler)(snapshot);
            }
            catch (Exception ex)
            {
                _subscriberFailureLog.Report(ex, "Monitoring: a snapshot subscriber failed");
            }
        }
    }
}
