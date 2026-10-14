namespace TouchPanel.Monitoring;

/// <summary>
/// Optional capability an <see cref="ISensorSource"/> exposes to distinguish a backend failure
/// (initialization or read) from a machine that simply lacks individual sensors (RF-012/RF-065).
/// The <see cref="MonitoringService"/> uses it to keep the <c>monitoring</c> subsystem health
/// accurate without treating legitimately missing sensors as a degradation.
/// </summary>
internal interface ISensorSourceHealth
{
    /// <summary>
    /// Gets the reason the most recent poll could not reach the hardware backend, or
    /// <see langword="null"/> when the backend was read successfully. Missing individual sensors
    /// are not failures.
    /// </summary>
    string? LastFailureReason { get; }
}
