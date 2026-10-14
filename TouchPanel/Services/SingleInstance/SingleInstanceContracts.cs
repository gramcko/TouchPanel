namespace TouchPanel.Services.SingleInstance;

/// <summary>
/// Owns the session-scoped exclusion primitive that marks this process as the primary
/// instance (RF-001). Disposing it releases ownership so the next launch can become primary.
/// </summary>
public interface ISingleInstanceLock : IDisposable
{
}

/// <summary>
/// Creates the session-scoped single-instance lock (DD-3). Injected into
/// <see cref="SingleInstanceService"/> so tests can simulate primary/secondary and failure
/// states without real kernel objects.
/// </summary>
public interface ISingleInstanceLockFactory
{
    /// <summary>
    /// Attempts to acquire the session lock.
    /// </summary>
    /// <returns>
    /// The lock when this process acquired it (primary instance); <see langword="null"/> when
    /// another process already holds it (secondary instance).
    /// </returns>
    /// <exception cref="Exception">
    /// Thrown when the lock cannot be created at all. The service treats this as a degraded,
    /// fail-open condition: it continues as primary and logs a warning.
    /// </exception>
    ISingleInstanceLock? TryAcquire();
}

/// <summary>
/// The DD-3 primary/secondary signaling channel: the primary listens for show-panel and exit
/// requests, later launches post one to the running instance through the same channel
/// implementation.
/// </summary>
public interface ISingleInstanceChannel : IDisposable
{
    /// <summary>
    /// Starts listening for show-panel and exit requests (primary side). After this call, each
    /// received request invokes the matching callback on the channel's thread. Repeated calls are
    /// ignored.
    /// </summary>
    /// <param name="onShowPanelRequested">Callback invoked once per show-panel request received.</param>
    /// <param name="onExitRequested">Callback invoked once per exit request received.</param>
    void StartListening(Action onShowPanelRequested, Action onExitRequested);

    /// <summary>
    /// Posts one show-panel request to the running instance (secondary side). Must be safe to
    /// call without a primary being present.
    /// </summary>
    /// <returns><see langword="true"/> when the running instance was found and accepted the request.</returns>
    bool TrySignalRunningInstance();

    /// <summary>
    /// Posts one exit request to the running instance (secondary side, RF-006). Must be safe to
    /// call without a primary being present.
    /// </summary>
    /// <returns><see langword="true"/> when the running instance was found and accepted the request.</returns>
    bool TrySignalExit();
}
