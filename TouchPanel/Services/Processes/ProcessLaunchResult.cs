namespace TouchPanel.Services.Processes;

/// <summary>
/// Outcome of a launch attempt. Launch failures are values, not exceptions, so the action layer can
/// contain and surface them (RF-050).
/// </summary>
/// <param name="Success">Whether the process was started.</param>
/// <param name="ErrorMessage">Why the launch failed; <see langword="null"/> on success.</param>
public sealed record ProcessLaunchResult(bool Success, string? ErrorMessage)
{
    /// <summary>Creates a successful launch result.</summary>
    /// <returns>The success result.</returns>
    public static ProcessLaunchResult Ok() => new(true, null);

    /// <summary>Creates a failed launch result.</summary>
    /// <param name="errorMessage">The failure reason (for example a <c>Win32Exception</c> message).</param>
    /// <returns>The failure result.</returns>
    public static ProcessLaunchResult Failed(string errorMessage) => new(false, errorMessage);
}
