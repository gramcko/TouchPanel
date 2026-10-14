using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Deterministic <see cref="ISensorHistory"/> double for history-driven widget tests (T-07): the
/// test seeds and mutates per-metric samples directly, so history→points mapping, gaps, scale, and
/// update-on-change can be asserted without any ring buffer, timer, or hardware. It also counts
/// reads and can be told to throw, to exercise the widget's render-failure degradation.
/// </summary>
internal sealed class FakeSensorHistory : ISensorHistory
{
    private readonly Dictionary<string, List<double?>> _buffers = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public int Capacity { get; set; } = 60;

    /// <inheritdoc />
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets the number of <see cref="GetHistory"/> calls observed.</summary>
    public int GetHistoryCallCount { get; private set; }

    /// <summary>Gets or sets an exception thrown by <see cref="GetHistory"/> instead of returning a slice.</summary>
    public Exception? GetHistoryException { get; set; }

    /// <inheritdoc />
    public bool IsTracked(string sensorKey) =>
        !string.IsNullOrWhiteSpace(sensorKey) && _buffers.ContainsKey(sensorKey.Trim());

    /// <inheritdoc />
    public IReadOnlyList<double?> GetHistory(string sensorKey)
    {
        GetHistoryCallCount++;

        if (GetHistoryException is { } exception)
        {
            throw exception;
        }

        var key = sensorKey?.Trim() ?? string.Empty;
        return _buffers.TryGetValue(key, out var buffer) ? buffer.ToArray() : Array.Empty<double?>();
    }

    /// <inheritdoc />
    public int GetCount(string sensorKey)
    {
        var key = sensorKey?.Trim() ?? string.Empty;
        return _buffers.TryGetValue(key, out var buffer) ? buffer.Count : 0;
    }

    /// <summary>Replaces the retained samples for a metric (oldest → newest; <see langword="null"/> is a gap).</summary>
    public void Set(string sensorKey, params double?[] samples)
    {
        _buffers[sensorKey.Trim()] = samples.ToList();
    }

    /// <summary>Appends one sample, dropping the oldest when the configured capacity is reached.</summary>
    public void Append(string sensorKey, double? sample)
    {
        var key = sensorKey.Trim();
        if (!_buffers.TryGetValue(key, out var buffer))
        {
            buffer = new List<double?>();
            _buffers[key] = buffer;
        }

        buffer.Add(sample);
        while (buffer.Count > Capacity)
        {
            buffer.RemoveAt(0);
        }
    }
}
