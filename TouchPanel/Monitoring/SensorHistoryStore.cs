namespace TouchPanel.Monitoring;

/// <summary>
/// The monitoring layer's bounded telemetry history (RF-069/M2-RF-691..697, plan MD-5). It owns one
/// fixed-capacity <see cref="RingBuffer{T}"/> of <c>double?</c> per tracked metric and is fed from
/// the same snapshots <see cref="MonitoringService.PublishOnce"/> already produces — there is no
/// second polling path (M2-RF-412/RNF-012).
/// </summary>
/// <remarks>
/// <para><b>Tracked metric set (M2-RF-693).</b> Buffers are created lazily by
/// <see cref="Track"/> only for the keys the current configuration references and released by
/// <see cref="Release"/>/<see cref="SetTracked"/> when a reload or page change no longer references
/// them; unreferenced per-core/fan keys therefore cost nothing.</para>
/// <para><b>Capacity (M2-RF-691).</b> Capacity =
/// <c>clamp(ceil(windowSeconds / intervalSeconds), 2, 3600)</c> and is recomputed by
/// <see cref="Reconfigure"/> when the polling interval or window changes; existing history is
/// preserved where it still fits and the oldest samples are dropped otherwise (M2-RF-695).</para>
/// <para><b>Threading (M2-RF-692).</b> <see cref="Record"/> runs on the polling thread and takes the
/// store lock; <see cref="GetHistory"/>/<see cref="GetCount"/> copy under the same lock so UI
/// readers never see torn data. The lock order is always store → ring buffer.</para>
/// <para><b>Memory (M2-RF-694/RNF-012).</b> A sample is a <see cref="Nullable{T}"/> of a double
/// (16 bytes, see <see cref="EstimatedBytesPerSample"/>). With <c>C</c> capacity and <c>M</c>
/// tracked metrics the worst-case retained payload is <c>C × M × 16</c> bytes (use
/// <see cref="EstimateMemoryBytes"/>); <c>C</c> is bounded by <see cref="MaximumCapacity"/> and
/// <c>M</c> is bounded by the number of configured metrics, so the total is bounded.</para>
/// </remarks>
public sealed class SensorHistoryStore : ISensorHistory, IDisposable
{
    /// <summary>Smallest accepted per-metric capacity (M2-RF-691).</summary>
    public const int MinimumCapacity = 2;

    /// <summary>Largest accepted per-metric capacity (M2-RF-691).</summary>
    public const int MaximumCapacity = 3600;

    /// <summary>Smallest accepted sparkline-style history window, in seconds (aligned with RF-041).</summary>
    public const double MinimumWindowSeconds = 10;

    /// <summary>Largest accepted sparkline-style history window, in seconds (aligned with RF-041).</summary>
    public const double MaximumWindowSeconds = 3600;

    /// <summary>Default history window, in seconds (aligned with RF-041's 60 s default).</summary>
    public const double DefaultWindowSeconds = 60;

    /// <summary>
    /// Approximate retained size of one <c>double?</c> sample in a <see cref="RingBuffer{T}"/>:
    /// an 8-byte value plus a presence flag/padding, aligned to 16 bytes. Used only for the
    /// documented memory bound (M2-RF-694).
    /// </summary>
    public const int EstimatedBytesPerSample = 16;

    private readonly object _gate = new();
    private readonly Dictionary<string, RingBuffer<double?>> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private double _windowSeconds;
    private TimeSpan _interval;
    private int _capacity;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SensorHistoryStore"/> class with a fixed window
    /// and polling interval.
    /// </summary>
    /// <param name="interval">The polling interval used to derive the capacity; must be positive.</param>
    /// <param name="windowSeconds">The history window in seconds; defaults to 60 s.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public SensorHistoryStore(TimeSpan interval, double windowSeconds = DefaultWindowSeconds)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The polling interval must be positive.");
        }

        _windowSeconds = windowSeconds;
        _interval = interval;
        _capacity = ComputeCapacity(windowSeconds, interval);
    }

    /// <summary>Gets the fixed number of samples retained per tracked metric (RF-069).</summary>
    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _capacity;
            }
        }
    }

    /// <summary>
    /// Gets the history window, in seconds, used to derive <see cref="Capacity"/> for the current
    /// polling interval (M2-RF-691). Updated by <see cref="Reconfigure(double, TimeSpan)"/>.
    /// </summary>
    public double WindowSeconds
    {
        get
        {
            lock (_gate)
            {
                return _windowSeconds;
            }
        }
    }

    /// <inheritdoc />
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

    /// <summary>Gets the number of metrics currently tracked (bounded by the configuration).</summary>
    public int TrackedCount
    {
        get
        {
            lock (_gate)
            {
                return _buffers.Count;
            }
        }
    }

    /// <summary>Gets a snapshot of the currently tracked metric keys.</summary>
    public IReadOnlyList<string> TrackedKeys
    {
        get
        {
            lock (_gate)
            {
                return _buffers.Keys.ToArray();
            }
        }
    }

    /// <summary>
    /// Computes the per-metric capacity for a history window and polling interval:
    /// <c>clamp(ceil(windowSeconds / intervalSeconds), MinimumCapacity, MaximumCapacity)</c>
    /// (M2-RF-691). Invalid or non-finite inputs degrade to <see cref="MinimumCapacity"/> rather
    /// than throwing, so hostile configuration cannot break monitoring.
    /// </summary>
    /// <param name="windowSeconds">The history window in seconds.</param>
    /// <param name="interval">The polling interval.</param>
    /// <returns>The bounded capacity.</returns>
    public static int ComputeCapacity(double windowSeconds, TimeSpan interval)
    {
        if (!double.IsFinite(windowSeconds) || windowSeconds <= 0 || interval <= TimeSpan.Zero)
        {
            return MinimumCapacity;
        }

        var intervalSeconds = interval.TotalSeconds;
        if (!double.IsFinite(intervalSeconds) || intervalSeconds <= 0)
        {
            return MinimumCapacity;
        }

        var samples = Math.Ceiling(windowSeconds / intervalSeconds);
        if (samples <= MinimumCapacity)
        {
            return MinimumCapacity;
        }

        return samples >= MaximumCapacity ? MaximumCapacity : (int)samples;
    }

    /// <summary>
    /// Estimates the worst-case retained payload of a history store (M2-RF-694):
    /// <c>capacity × trackedMetricCount × </c><see cref="EstimatedBytesPerSample"/>. Backing-array
    /// and per-buffer object overhead are small and not included.
    /// </summary>
    /// <param name="capacity">The per-metric capacity.</param>
    /// <param name="trackedMetricCount">The number of tracked metrics.</param>
    /// <returns>The estimated payload in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either argument is negative.</exception>
    public static long EstimateMemoryBytes(int capacity, int trackedMetricCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(trackedMetricCount);
        return (long)capacity * trackedMetricCount * EstimatedBytesPerSample;
    }

    /// <inheritdoc />
    public bool IsTracked(string sensorKey)
    {
        var key = NormalizeKey(sensorKey);
        if (key.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            return _buffers.ContainsKey(key);
        }
    }

    /// <summary>
    /// Starts tracking a metric, creating its fixed-capacity buffer lazily. Tracking an
    /// already-tracked key (case-insensitively) is a no-op.
    /// </summary>
    /// <param name="sensorKey">The application-level sensor key; blank keys are rejected.</param>
    /// <returns><see langword="true"/> when a new buffer was created; <see langword="false"/> when the key was already tracked.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sensorKey"/> is blank.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the store was disposed.</exception>
    public bool Track(string sensorKey)
    {
        var key = NormalizeRequiredKey(sensorKey);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_buffers.ContainsKey(key))
            {
                return false;
            }

            _buffers[key] = new RingBuffer<double?>(_capacity);
            return true;
        }
    }

    /// <summary>
    /// Releases a metric's buffer and all retained samples. Releasing an untracked key is a no-op.
    /// </summary>
    /// <param name="sensorKey">The application-level sensor key.</param>
    /// <returns><see langword="true"/> when a buffer was released.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the store was disposed.</exception>
    public bool Release(string sensorKey)
    {
        var key = NormalizeKey(sensorKey);
        if (key.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffers.Remove(key);
        }
    }

    /// <summary>
    /// Reconciles the tracked set against the metrics the current configuration references
    /// (M2-RF-693): missing keys are tracked lazily and keys no longer present are released. Used
    /// on config reload/page change so unreferenced buffers cost nothing.
    /// </summary>
    /// <param name="sensorKeys">The metric keys referenced by the current configuration; blank entries are ignored.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sensorKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the store was disposed.</exception>
    public void SetTracked(IEnumerable<string> sensorKeys)
    {
        ArgumentNullException.ThrowIfNull(sensorKeys);

        var desired = new HashSet<string>(
            sensorKeys
                .Select(NormalizeKey)
                .Where(key => key.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            foreach (var key in desired)
            {
                if (!_buffers.ContainsKey(key))
                {
                    _buffers[key] = new RingBuffer<double?>(_capacity);
                }
            }

            foreach (var key in _buffers.Keys.ToArray())
            {
                if (!desired.Contains(key))
                {
                    _buffers.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// Recomputes the per-metric capacity for a new window/interval, preserving as much existing
    /// history as still fits and dropping the oldest samples otherwise (M2-RF-695).
    /// </summary>
    /// <param name="windowSeconds">The history window in seconds.</param>
    /// <param name="interval">The polling interval.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the store was disposed.</exception>
    public void Reconfigure(double windowSeconds, TimeSpan interval)
    {
        var capacity = ComputeCapacity(windowSeconds, interval);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _windowSeconds = windowSeconds;
            _interval = interval;
            if (capacity == _capacity)
            {
                return;
            }

            _capacity = capacity;
            foreach (var buffer in _buffers.Values)
            {
                buffer.Resize(capacity);
            }
        }
    }

    /// <summary>
    /// Recomputes the per-metric capacity for a new polling interval while keeping the window this
    /// store was configured with, preserving as much existing history as still fits and dropping
    /// the oldest samples otherwise (M2-RF-695/M2-RF-712). Used when the telemetry interval changes
    /// live.
    /// </summary>
    /// <param name="interval">The polling interval.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the store was disposed.</exception>
    public void Reconfigure(TimeSpan interval)
    {
        double windowSeconds;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            windowSeconds = _windowSeconds;
        }

        Reconfigure(windowSeconds, interval);
    }

    /// <summary>
    /// Records one snapshot into every tracked metric's buffer, on the polling thread. A tracked
    /// sensor that is missing or unavailable is stored as an explicit <see langword="null"/> gap,
    /// never as zero (M2-RF-696).
    /// </summary>
    /// <param name="snapshot">The snapshot produced by the monitoring loop; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the store was disposed.</exception>
    public void Record(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            foreach (var (key, buffer) in _buffers)
            {
                if (snapshot.TryGetReading(key, out var reading) && reading.IsAvailable)
                {
                    buffer.Add(reading.Value);
                }
                else
                {
                    buffer.Add(null);
                }
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<double?> GetHistory(string sensorKey)
    {
        var key = NormalizeKey(sensorKey);
        if (key.Length == 0)
        {
            return Array.Empty<double?>();
        }

        lock (_gate)
        {
            return _buffers.TryGetValue(key, out var buffer)
                ? buffer.ToArray()
                : Array.Empty<double?>();
        }
    }

    /// <inheritdoc />
    public int GetCount(string sensorKey)
    {
        var key = NormalizeKey(sensorKey);
        if (key.Length == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            return _buffers.TryGetValue(key, out var buffer) ? buffer.Count : 0;
        }
    }

    /// <summary>
    /// Clears every tracked buffer and releases references to their samples while keeping the
    /// tracked keys. Kept separate from <see cref="Dispose"/> so a caller can reset history without
    /// tearing down tracking.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var buffer in _buffers.Values)
            {
                buffer.Clear();
            }
        }
    }

    /// <summary>
    /// Clears and releases every history buffer (RF-069/M2-RF-697): no sample or buffer reference
    /// survives disposal. Further mutating calls throw; reads degrade to empty.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var buffer in _buffers.Values)
            {
                buffer.Clear();
            }

            _buffers.Clear();
        }
    }

    private static string NormalizeKey(string? sensorKey) =>
        string.IsNullOrWhiteSpace(sensorKey) ? string.Empty : sensorKey.Trim();

    private static string NormalizeRequiredKey(string sensorKey)
    {
        var key = NormalizeKey(sensorKey);
        if (key.Length == 0)
        {
            throw new ArgumentException("A sensor key is required.", nameof(sensorKey));
        }

        return key;
    }
}
