namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Minimal manual <see cref="TimeProvider"/> fake (DD-14): tests set or advance
/// <see cref="UtcNow"/> explicitly instead of waiting on wall-clock time, and timers created
/// through <see cref="CreateTimer"/> fire synchronously when their due time is reached. This
/// keeps retry/backoff schedules fully deterministic.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = new();

    /// <summary>Gets or sets the value returned by <see cref="GetUtcNow"/>.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    /// <remarks>Pinned to UTC so local-time consumers (the Clock widget) are deterministic in tests.</remarks>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Gets the number of timers created through this provider that are not disposed yet.</summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether any live timer has a scheduled due time. Tests use this to
    /// wait until an asynchronous loop has actually armed its next tick before advancing the clock.
    /// </summary>
    public bool HasScheduledTimer
    {
        get
        {
            lock (_gate)
            {
                return _timers.Any(timer => timer.IsScheduled);
            }
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => UtcNow;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new FakeTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Advances the clock by <paramref name="amount"/> and synchronously fires every timer whose
    /// due time is reached in order; callbacks may schedule further due timers.
    /// </summary>
    /// <param name="amount">The amount to advance; negative values are rejected.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Time can only move forward.");
        }

        lock (_gate)
        {
            UtcNow += amount;
        }

        FireDueTimers();
    }

    private void FireDueTimers()
    {
        // Callbacks may schedule or reschedule timers, so keep firing while anything is due.
        for (var guard = 0; guard < 10_000; guard++)
        {
            FakeTimer? due;
            lock (_gate)
            {
                var now = UtcNow;
                due = _timers
                    .Where(timer => timer.IsDue(now))
                    .OrderBy(timer => timer.DueAt)
                    .FirstOrDefault();
                due?.MarkFired();
            }

            if (due is null)
            {
                return;
            }

            due.Invoke();
        }
    }

    private void RemoveTimer(FakeTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly FakeTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _dueAt;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _disposed;

        public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset? DueAt
        {
            get
            {
                lock (_owner._gate)
                {
                    return _dueAt;
                }
            }
        }

        public bool IsDue(DateTimeOffset now)
        {
            lock (_owner._gate)
            {
                return !_disposed && _dueAt is { } due && due <= now;
            }
        }

        /// <summary>Gets a value indicating whether the timer has a pending due time.</summary>
        public bool IsScheduled
        {
            get
            {
                lock (_owner._gate)
                {
                    return !_disposed && _dueAt is not null;
                }
            }
        }

        /// <summary>Moves the timer to its next occurrence (or disables it) before the callback runs.</summary>
        /// <remarks>Must be called while holding the owner's gate.</remarks>
        public void MarkFired()
        {
            if (_disposed)
            {
                _dueAt = null;
                return;
            }

            _dueAt = _period > TimeSpan.Zero ? _dueAt + _period : null;
        }

        public void Invoke()
        {
            if (_disposed)
            {
                return;
            }

            _callback(_state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                _period = period;

                if (_disposed)
                {
                    return false;
                }

                _dueAt = dueTime < TimeSpan.Zero ? null : _owner.UtcNow + dueTime;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                _disposed = true;
                _dueAt = null;
            }

            _owner.RemoveTimer(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
