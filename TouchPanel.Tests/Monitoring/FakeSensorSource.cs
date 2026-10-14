using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Deterministic <see cref="ISensorSource"/> for monitoring tests (RNF-006): returns
/// caller-supplied snapshots, can simulate a failing poll, and records whether it was disposed.
/// </summary>
internal sealed class FakeSensorSource : ISensorSource, ISensorSourceHealth, IDisposable
{
    private int _pollCount;

    /// <summary>Gets or sets the snapshot factory, receiving the one-based poll number.</summary>
    public Func<int, SensorSnapshot>? SnapshotFactory { get; set; }

    /// <summary>Gets or sets an exception thrown for every poll while set.</summary>
    public Exception? PollException { get; set; }

    /// <summary>
    /// Gets or sets the backend failure the source reports for the <c>monitoring</c> subsystem
    /// health (RF-012); <see langword="null"/> means the backend was reachable.
    /// </summary>
    public string? LastFailureReason { get; set; }

    /// <summary>Gets the number of polls performed so far.</summary>
    public int PollCount => Volatile.Read(ref _pollCount);

    /// <summary>Gets a value indicating whether the source was disposed.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public SensorSnapshot Poll()
    {
        var pollNumber = Interlocked.Increment(ref _pollCount);
        if (PollException is { } exception)
        {
            throw exception;
        }

        return SnapshotFactory?.Invoke(pollNumber)
            ?? SensorSnapshot.CreateUnavailable(DateTimeOffset.UnixEpoch);
    }

    /// <inheritdoc />
    public void Dispose() => Disposed = true;
}
