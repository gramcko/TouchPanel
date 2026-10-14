namespace TouchPanel.Monitoring;

/// <summary>
/// WPF-free source of the selectable sensor keys offered by the edit-mode sensor picker
/// (M5-RF-510). Implementations build the list over the stable <see cref="SensorKeys"/> model and,
/// when a monitoring snapshot is available, discover the per-core CPU (<c>cpu.core.&lt;n&gt;</c>) and
/// fan (<c>fan.&lt;n&gt;</c>) families. The seam lives in the monitoring layer so it is unit-testable
/// without hardware and without WPF.
/// </summary>
public interface ISensorCatalog
{
    /// <summary>
    /// Gets the selectable sensor keys for the current machine. The fixed aggregate keys are always
    /// present (so the list is never empty); per-core and fan keys are appended when a snapshot is
    /// available.
    /// </summary>
    /// <returns>The selectable sensor options in a deterministic order; never empty.</returns>
    IReadOnlyList<SensorOption> GetSensorOptions();

    /// <summary>
    /// Gets the selectable sensor keys, guaranteeing that a configured (possibly unknown) key is
    /// present so the picker can display and preserve it (M5-RF-512). When
    /// <paramref name="currentSensorKey"/> is already in the catalog it is not duplicated.
    /// </summary>
    /// <param name="currentSensorKey">The widget's configured sensor key, or <see langword="null"/>.</param>
    /// <returns>The selectable sensor options; never empty.</returns>
    IReadOnlyList<SensorOption> GetSensorOptions(string? currentSensorKey);
}
