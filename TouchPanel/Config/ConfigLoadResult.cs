namespace TouchPanel.Config;

/// <summary>
/// Outcome of loading <c>panel.json</c>: which of the four RF-075 states was detected, the
/// effective configuration when one is available, and the diagnostics produced along the way.
/// </summary>
/// <remarks>
/// <see cref="Config"/> is the configuration the caller should run with. It is <see langword="null"/>
/// only when no effective configuration could be produced (for example a fully unreadable source
/// with no usable backup); in the recoverable states the store supplies the backup or generated
/// defaults instead.
/// </remarks>
public sealed record ConfigLoadResult
{
    /// <summary>Gets the detected load state (RF-075).</summary>
    public required ConfigState State { get; init; }

    /// <summary>Gets the effective configuration to run with, or <see langword="null"/> when none is available.</summary>
    public PanelConfig? Config { get; init; }

    /// <summary>
    /// Gets the schema version found in the source file, when it could be read. Helps diagnostics
    /// distinguish a missing version (<see langword="null"/>) from an explicit unsupported one.
    /// </summary>
    public int? DetectedSchemaVersion { get; init; }

    /// <summary>Gets the diagnostics produced during the load; never <see langword="null"/>.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; init; } = [];
}
