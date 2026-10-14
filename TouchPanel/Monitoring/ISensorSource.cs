namespace TouchPanel.Monitoring;

/// <summary>
/// The seam between the polling loop and the hardware backend (DD-10/RF-062): one synchronous
/// poll that never throws and always returns an immutable <see cref="SensorSnapshot"/>. The
/// production implementation is <see cref="LibreHardwareMonitorAdapter"/>; tests use a fake source
/// so no hardware, driver, or elevation is ever required (RNF-006).
/// </summary>
internal interface ISensorSource
{
    /// <summary>
    /// Reads the current hardware state and projects it into a snapshot. Implementations must
    /// contain every failure and mark affected fields unavailable instead of throwing (RF-065).
    /// </summary>
    /// <returns>The captured snapshot; never <see langword="null"/>.</returns>
    SensorSnapshot Poll();
}
