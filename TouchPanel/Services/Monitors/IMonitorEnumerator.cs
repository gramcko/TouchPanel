namespace TouchPanel.Services.Monitors;

/// <summary>
/// Plan section 1.3 boundary for monitor discovery (RF-015/RF-016): returns a deterministic list of
/// <see cref="MonitorDescriptor"/> values with stable identity fields and physical bounds. The
/// production implementation is <c>Win32MonitorEnumerator</c> in <c>Interop/</c>; tests inject a
/// fake so matcher and selection logic run without hardware (RNF-006).
/// </summary>
public interface IMonitorEnumerator
{
    /// <summary>
    /// Enumerates the active monitors. Implementations must not throw for expected native failures:
    /// the failure is logged and the usable subset is returned (an empty list when nothing can be
    /// enumerated). The result must be ordered deterministically (stable identity fields) so the
    /// matcher's stable-order tiebreak is reproducible (RF-016).
    /// </summary>
    /// <returns>The active monitors, in a deterministic order; never <see langword="null"/>.</returns>
    IReadOnlyList<MonitorDescriptor> Enumerate();
}
