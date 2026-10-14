namespace TouchPanel.Monitoring;

/// <summary>
/// The read-only snapshot boundary consumed by data-driven widgets (RF-062/RF-067). A widget may
/// subscribe to snapshots and read the latest value but can neither start, stop, nor dispose the
/// monitoring pipeline that owns the hardware library.
/// </summary>
public interface ISensorSnapshotSource
{
    /// <summary>Gets the most recently published snapshot, or <see langword="null"/> before the first poll.</summary>
    SensorSnapshot? Latest { get; }

    /// <summary>
    /// Raised on the background polling thread for every published snapshot (about 1 Hz, RF-064).
    /// Subscribers that touch UI state must marshal to the UI thread themselves (RF-067).
    /// </summary>
    event Action<SensorSnapshot>? SnapshotAvailable;
}

/// <summary>
/// The application-level monitoring service (plan section 1.3, RF-062..RF-068): publishes
/// <see cref="SensorSnapshot"/> values on a configurable background cadence (RF-071), starts/stops
/// with the application lifecycle, and disposes the hardware backend cleanly.
/// </summary>
public interface IMonitoringService : ISensorSnapshotSource, IDisposable
{
    /// <summary>Gets the configured polling interval.</summary>
    TimeSpan Interval { get; }

    /// <summary>Gets a value indicating whether the background polling loop is running.</summary>
    bool IsRunning { get; }

    /// <summary>Starts polling. Safe to call repeatedly; a running service is unaffected.</summary>
    void Start();

    /// <summary>
    /// Applies a new polling interval live (RF-071): clamps to the accepted range, recomputes the
    /// bounded history capacity, and restarts the single background loop when running so the new
    /// cadence takes effect without a restart or a duplicate loop.
    /// </summary>
    /// <param name="interval">The requested interval; clamped when outside the accepted range.</param>
    void ApplyInterval(TimeSpan interval);

    /// <summary>
    /// Stops polling and cancels the background loop. Safe to call repeatedly; the call returns
    /// after the loop task has stopped (bounded wait) so no timers or tasks are left alive.
    /// </summary>
    void Stop();
}
