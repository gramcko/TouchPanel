namespace TouchPanel.Services.AppLifecycle;

/// <summary>
/// A subsystem that takes part in the ordered application shutdown (RF-007, plan section 1.2).
/// Participants are invoked exactly once by <see cref="AppLifecycleCoordinator"/> in registration
/// order (dependents before their dependencies) and receive the shared shutdown cancellation
/// token so they can abandon work when the total budget expires.
/// <para>
/// Implementations must not require the WPF dispatcher to complete their cleanup: the coordinator
/// runs the sequence synchronously at exit, so a participant that needs the UI thread would stall
/// until the budget expires. Exceptions are isolated and logged; they never propagate.
/// </para>
/// </summary>
public interface IApplicationShutdownParticipant
{
    /// <summary>Gets the stable participant name used in shutdown logs and registration-conflict errors.</summary>
    string Name { get; }

    /// <summary>
    /// Performs the participant's cleanup. The cancellation token is cancelled when the total
    /// shutdown budget elapses or when the caller cancels the shutdown run.
    /// </summary>
    /// <param name="cancellationToken">Signals that the participant must abandon remaining work.</param>
    /// <returns>A task that completes when the cleanup is done.</returns>
    ValueTask ShutdownAsync(CancellationToken cancellationToken);
}
