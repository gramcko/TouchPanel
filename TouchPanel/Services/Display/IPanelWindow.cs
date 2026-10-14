using TouchPanel.Services.Monitors;

namespace TouchPanel.Services.Display;

/// <summary>
/// The subset of panel-window operations the display-change handling needs, abstracted so the
/// RF-017/RF-022 policy can be applied and asserted in tests without a real WPF window (RNF-006).
/// Implemented by <c>MainWindow</c>; every method uses the non-activating Win32 paths (RF-018).
/// </summary>
public interface IPanelWindow
{
    /// <summary>Gets a value indicating whether the panel is currently shown.</summary>
    bool IsVisible { get; }

    /// <summary>
    /// Gets the DPI scale (physical pixels per DIP) of the monitor the panel currently occupies.
    /// The display-change handler divides the target monitor's physical bounds by this scale to
    /// derive the DIP page bounds widgets are clamped against (RF-022). Must be positive; values
    /// that are non-finite or non-positive are treated as 1.0 by the caller.
    /// </summary>
    double DpiScale { get; }

    /// <summary>
    /// Places the panel on the monitor's full physical bounds without activation (RF-014/RF-028,
    /// DD-5). Safe to call while the window is hidden.
    /// </summary>
    /// <param name="monitor">The monitor whose bounds the panel must occupy.</param>
    void PlaceOnMonitor(MonitorDescriptor monitor);

    /// <summary>
    /// Applies the always-on-top behavior without activation (RF-020).
    /// </summary>
    /// <param name="topmost">Whether the panel should stay above non-topmost windows.</param>
    void SetTopmost(bool topmost);

    /// <summary>Shows the panel without activating it (RF-018/RF-024).</summary>
    void ShowWithoutActivation();

    /// <summary>Hides the panel without activating anything (RF-006/RF-024).</summary>
    void HidePanel();
}
