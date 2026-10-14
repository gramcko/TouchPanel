using Serilog;

namespace TouchPanel.Monitoring;

/// <summary>
/// Bounded, throttled re-initialization of a failed monitoring backend (RF-070/M2-RF-701..704, plan
/// MD-7). It owns no timer of its own: the <see cref="MonitoringService"/> polling loop asks it to
/// recover when the source reports a backend failure, and this component decides — from the
/// injected <see cref="TimeProvider"/> — whether the retry cadence has elapsed. Attempts are made
/// at most once per <see cref="DefaultRetryInterval"/> (default five minutes), which is measured
/// from the last time the backend was known healthy so the very first failure is not immediately
/// re-opened twice. Failed attempts are logged at a throttled rate so a backend that never recovers
/// stays observable without log spam, and re-initialization never requests elevation (RF-068/RF-086)
/// and never runs on the UI thread.
/// </summary>
internal sealed class BackendReinitializer
{
    /// <summary>The default retry cadence: at most one re-open attempt every five minutes (M2-RF-701).</summary>
    public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromMinutes(5);

    private readonly Func<bool> _tryReinitialize;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retryInterval;
    private readonly ThrottledFailureLogger _failureLog;
    private readonly object _gate = new();
    private DateTimeOffset _lastAttemptAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackendReinitializer"/> class.
    /// </summary>
    /// <param name="tryReinitialize">
    /// The re-open operation invoked on the background polling thread; returns <see langword="true"/>
    /// when the backend is reachable again. It must not throw (a throw is caught and treated as a
    /// failed attempt), and it must contain its own failures (RF-065).
    /// </param>
    /// <param name="logger">Logger for retry attempts; defaults handled by the caller.</param>
    /// <param name="timeProvider">Clock used for the retry cadence and the log throttle; defaults to the system clock.</param>
    /// <param name="retryInterval">The minimum interval between attempts; defaults to <see cref="DefaultRetryInterval"/>.</param>
    /// <param name="failureLogThrottle">The throttle window for failed-attempt logs; defaults to one minute.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tryReinitialize"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="retryInterval"/> is negative.</exception>
    public BackendReinitializer(
        Func<bool> tryReinitialize,
        ILogger logger,
        TimeProvider? timeProvider = null,
        TimeSpan? retryInterval = null,
        TimeSpan? failureLogThrottle = null)
    {
        ArgumentNullException.ThrowIfNull(tryReinitialize);
        ArgumentNullException.ThrowIfNull(logger);

        _tryReinitialize = tryReinitialize;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retryInterval = retryInterval ?? DefaultRetryInterval;

        if (_retryInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryInterval), _retryInterval, "The retry interval must not be negative.");
        }

        _failureLog = new ThrottledFailureLogger(_logger, _timeProvider, failureLogThrottle);

        // A brand-new backend has not failed yet; anchoring the window at construction means the
        // first detected failure is retried one cadence later instead of being re-opened twice.
        _lastAttemptAt = _timeProvider.GetUtcNow();
    }

    /// <summary>Gets the minimum interval between re-initialization attempts.</summary>
    public TimeSpan RetryInterval => _retryInterval;

    /// <summary>
    /// Notes that the backend was polled successfully, re-anchoring the retry window so a later
    /// failure is retried one cadence after the last healthy poll (M2-RF-701). Called by the
    /// monitoring loop on every successful poll.
    /// </summary>
    public void OnBackendHealthy()
    {
        lock (_gate)
        {
            _lastAttemptAt = _timeProvider.GetUtcNow();
        }
    }

    /// <summary>
    /// Attempts to re-initialize the backend when the retry window has elapsed. Returns
    /// <see langword="true"/> only when the backend recovered. A throttled, non-positive result is
    /// returned while the window is still open, so the polling loop never busy-retries.
    /// </summary>
    /// <returns><see langword="true"/> when the backend is reachable again; otherwise <see langword="false"/>.</returns>
    public bool TryRecover()
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            if (now - _lastAttemptAt < _retryInterval)
            {
                return false;
            }

            // Record the attempt before releasing the lock so a concurrent caller observes the
            // throttle even though the (potentially slow) re-open runs outside it.
            _lastAttemptAt = now;
        }

        bool recovered;
        try
        {
            recovered = _tryReinitialize();
        }
        catch (Exception ex)
        {
            // The operation contract is never-throw; a defective delegate must still not take the
            // polling loop down (RF-012/RF-065).
            recovered = false;
            _failureLog.Report(ex, "Monitoring: re-initializing the hardware backend failed");
        }

        if (recovered)
        {
            _logger.Information("Monitoring: the hardware backend re-initialized successfully");
            return true;
        }

        _failureLog.Report(
            null,
            "Monitoring: the hardware backend is still unavailable; re-initialization is throttled");
        return false;
    }
}
