using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Deterministic <see cref="ISensorSnapshotSource"/> for widget tests: the test controls exactly
/// when a snapshot is published, so gauge mapping, change detection, and unsubscription can be
/// asserted without any timer or hardware.
/// </summary>
internal sealed class FakeSnapshotSource : ISensorSnapshotSource
{
    /// <inheritdoc />
    public SensorSnapshot? Latest { get; private set; }

    /// <inheritdoc />
    public event Action<SensorSnapshot>? SnapshotAvailable;

    /// <summary>Gets the number of snapshots published so far.</summary>
    public int PublishCount { get; private set; }

    /// <summary>
    /// Sets <see cref="Latest"/> and raises <see cref="SnapshotAvailable"/> synchronously on the
    /// calling thread, mimicking the monitoring background thread.
    /// </summary>
    /// <param name="snapshot">The snapshot to publish.</param>
    public void Publish(SensorSnapshot snapshot)
    {
        Latest = snapshot;
        PublishCount++;
        SnapshotAvailable?.Invoke(snapshot);
    }
}
