using Serilog;

namespace TouchPanel.Monitoring;

/// <summary>
/// A failure-log throttle for long-running polling loops (RF-065): the first failure is logged
/// immediately, repeated failures within the throttle interval only increment a suppressed counter,
/// and the next logged failure reports how many were suppressed. Timing is driven by the injected
/// BCL <see cref="TimeProvider"/> so the schedule is unit-testable without wall-clock waits (DD-14).
/// </summary>
internal sealed class ThrottledFailureLogger
{
    /// <summary>The default throttle window: at most one failure log per minute per source.</summary>
    public static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minimumInterval;
    private readonly object _gate = new();
    private DateTimeOffset _lastLoggedAt = DateTimeOffset.MinValue;
    private int _suppressedSinceLastLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottledFailureLogger"/> class.
    /// </summary>
    /// <param name="logger">The logger failures are written to.</param>
    /// <param name="timeProvider">Clock used to measure the throttle window; defaults to the system clock.</param>
    /// <param name="minimumInterval">The throttle window; defaults to <see cref="DefaultMinimumInterval"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minimumInterval"/> is negative.</exception>
    public ThrottledFailureLogger(ILogger logger, TimeProvider? timeProvider = null, TimeSpan? minimumInterval = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _minimumInterval = minimumInterval ?? DefaultMinimumInterval;

        if (_minimumInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumInterval), _minimumInterval, "The throttle interval must not be negative.");
        }
    }

    /// <summary>Gets the throttle window between logged failures.</summary>
    public TimeSpan MinimumInterval => _minimumInterval;

    /// <summary>
    /// Logs the failure as a warning unless a failure was logged within
    /// <see cref="MinimumInterval"/>; suppressed counts are attached to the next log entry.
    /// </summary>
    /// <param name="exception">The failure, or <see langword="null"/> when only a message is available.</param>
    /// <param name="messageTemplate">The Serilog message template.</param>
    /// <param name="propertyValues">Template property values.</param>
    public void Report(Exception? exception, string messageTemplate, params object?[] propertyValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTemplate);
        ArgumentNullException.ThrowIfNull(propertyValues);

        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            if (now - _lastLoggedAt < _minimumInterval)
            {
                _suppressedSinceLastLog++;
                return;
            }

            _lastLoggedAt = now;
            var suppressed = _suppressedSinceLastLog;
            _suppressedSinceLastLog = 0;

            if (suppressed == 0)
            {
                _logger.Warning(exception, messageTemplate, propertyValues);
                return;
            }

            var values = new object?[propertyValues.Length + 1];
            Array.Copy(propertyValues, values, propertyValues.Length);
            values[^1] = suppressed;
            _logger.Warning(exception, messageTemplate + " (SuppressedCount={SuppressedCount})", values);
        }
    }
}
