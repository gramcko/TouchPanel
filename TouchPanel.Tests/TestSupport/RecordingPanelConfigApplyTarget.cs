using TouchPanel.Config;
using TouchPanel.Services.Config;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Recording <see cref="IPanelConfigApplyTarget"/> double (M2-RNF-124): records the order in which
/// the shared apply pipeline drove each step, keeps the last configuration seen per step, and can
/// simulate a per-step failure so the pipeline's isolation behavior is testable without WPF.
/// </summary>
internal sealed class RecordingPanelConfigApplyTarget : IPanelConfigApplyTarget
{
    /// <summary>Gets the apply steps in invocation order (stable keys: window-options, monitor-target, active-page, telemetry-interval, tracked-metrics, hotkeys, toggle-state).</summary>
    public List<string> Steps { get; } = [];

    /// <summary>Gets the last window section applied.</summary>
    public WindowConfig? LastWindow { get; private set; }

    /// <summary>Gets the last monitor section applied.</summary>
    public MonitorConfig? LastMonitor { get; private set; }

    /// <summary>Gets the last appearance section applied.</summary>
    public AppearanceConfig? LastAppearance { get; private set; }

    /// <summary>Gets the last configuration passed to <see cref="RebuildActivePage"/>.</summary>
    public PanelConfig? LastRebuilt { get; private set; }

    /// <summary>Gets the last telemetry section applied.</summary>
    public TelemetryConfig? LastTelemetry { get; private set; }

    /// <summary>Gets the last configuration passed to <see cref="ReconcileTrackedMetrics"/>.</summary>
    public PanelConfig? LastTracked { get; private set; }

    /// <summary>Gets the last hotkey list applied.</summary>
    public IReadOnlyList<HotkeyConfig>? LastHotkeys { get; private set; }

    /// <summary>Gets the last configuration passed to <see cref="ReconcileToggleState"/>.</summary>
    public PanelConfig? LastToggleReconciled { get; private set; }

    /// <summary>Gets or sets a step key that should throw when invoked, to exercise isolation.</summary>
    public string? FailStep { get; set; }

    /// <inheritdoc />
    public void ApplyWindowOptions(WindowConfig window)
    {
        Record("window-options");
        LastWindow = window;
    }

    /// <inheritdoc />
    public void ApplyMonitorTarget(MonitorConfig monitor)
    {
        Record("monitor-target");
        LastMonitor = monitor;
    }

    /// <inheritdoc />
    public void ApplyAppearance(AppearanceConfig appearance)
    {
        Record("appearance");
        LastAppearance = appearance;
    }

    /// <inheritdoc />
    public void RebuildActivePage(PanelConfig config)
    {
        Record("active-page");
        LastRebuilt = config;
    }

    /// <inheritdoc />
    public void ApplyTelemetryOptions(TelemetryConfig telemetry)
    {
        Record("telemetry-interval");
        LastTelemetry = telemetry;
    }

    /// <inheritdoc />
    public void ReconcileTrackedMetrics(PanelConfig config)
    {
        Record("tracked-metrics");
        LastTracked = config;
    }

    /// <inheritdoc />
    public void ApplyHotKeys(IReadOnlyList<HotkeyConfig> hotkeys)
    {
        Record("hotkeys");
        LastHotkeys = hotkeys;
    }

    /// <inheritdoc />
    public void ReconcileToggleState(PanelConfig config)
    {
        Record("toggle-state");
        LastToggleReconciled = config;
    }

    private void Record(string step)
    {
        if (string.Equals(step, FailStep, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Simulated failure in step '{step}'.");
        }

        Steps.Add(step);
    }
}
