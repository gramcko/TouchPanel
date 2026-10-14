namespace TouchPanel.Services.Processes;

/// <summary>
/// Starts a validated process request (plan section 1.3 boundary, RF-049/RF-052). Implementations
/// start the process and return immediately: launched processes are external and are never tracked,
/// waited on, or terminated (D-20).
/// </summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Starts the requested process without waiting for it (RF-052/D-20). Launch failures are
    /// returned as a failed <see cref="ProcessLaunchResult"/> and never thrown (RF-050); a
    /// cancellation requested before the launch surfaces as <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="request">The validated request built by <c>ProcessLaunchRequestFactory</c>.</param>
    /// <param name="cancellationToken">Cancels a launch that has not started yet.</param>
    /// <returns>The launch result; never <see langword="null"/>.</returns>
    Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default);
}
