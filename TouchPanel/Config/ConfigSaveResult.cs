namespace TouchPanel.Config;

/// <summary>
/// Outcome of a configuration save: whether the atomic write completed, plus diagnostics for the
/// failures that made it fail. A failed save always leaves the previous <c>panel.json</c> intact
/// and is retryable on the next save or shutdown (RF-077).
/// </summary>
public sealed record ConfigSaveResult
{
    /// <summary>Gets a value indicating whether the configuration file was replaced successfully.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets the diagnostics describing a failure; empty on success.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Creates a successful result.</summary>
    /// <returns>The result.</returns>
    public static ConfigSaveResult Success() => new() { Succeeded = true };

    /// <summary>Creates a failed result with one error diagnostic.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Human-readable failure description (already safe to log).</param>
    /// <returns>The result.</returns>
    public static ConfigSaveResult Failure(string code, string message) =>
        new()
        {
            Succeeded = false,
            Diagnostics = [ConfigDiagnostic.Error(code, message)],
        };
}
