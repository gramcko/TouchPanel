namespace TouchPanel.Monitoring;

/// <summary>
/// The read-only bounded-history boundary consumed by data-driven widgets such as the Sparkline
/// (RF-041/M2-RF-412, plan section 1.3). A widget can read the retained samples for a metric but
/// cannot add, remove, resize, record, or otherwise touch the monitoring pipeline; the write path
/// lives on the concrete <see cref="SensorHistoryStore"/> owned by the monitoring layer.
/// </summary>
/// <remarks>
/// Reads return a consistent copy so the UI thread never observes torn data (M2-RF-692). Querying a
/// metric that is not currently tracked returns an empty history rather than throwing, so a widget
/// whose sensor was released degrades to "Unavailable" instead of failing.
/// </remarks>
public interface ISensorHistory
{
    /// <summary>
    /// Gets the fixed number of samples retained per tracked metric.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the polling interval backing <see cref="Capacity"/>. Time-windowed consumers (the
    /// Sparkline) use it to map their own history window onto the number of retained samples that
    /// correspond to it, so widgets with different windows stay correct while sharing one store.
    /// </summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Gets a value indicating whether the metric currently has a history buffer.
    /// </summary>
    /// <param name="sensorKey">The application-level sensor key (see <see cref="SensorKeys"/>).</param>
    /// <returns><see langword="true"/> when the key is tracked.</returns>
    bool IsTracked(string sensorKey);

    /// <summary>
    /// Returns a consistent copy of the retained samples for a metric, ordered oldest → newest.
    /// Unavailable samples are represented by <see langword="null"/> (a gap), never by zero.
    /// </summary>
    /// <param name="sensorKey">The application-level sensor key (see <see cref="SensorKeys"/>).</param>
    /// <returns>The retained samples, or an empty list when the key is not tracked.</returns>
    IReadOnlyList<double?> GetHistory(string sensorKey);

    /// <summary>
    /// Gets the number of samples currently retained for a metric, or zero when it is not tracked.
    /// </summary>
    /// <param name="sensorKey">The application-level sensor key (see <see cref="SensorKeys"/>).</param>
    /// <returns>The retained sample count (0..<see cref="Capacity"/>).</returns>
    int GetCount(string sensorKey);
}
