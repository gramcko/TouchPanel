using TouchPanel.Config;

namespace TouchPanel.Services.Config;

/// <summary>
/// Apply target used only while the first-run setup wizard persists its choices. The wizard runs
/// before the panel is placed and before the page is rendered, so there is nothing live to apply;
/// the startup path (<c>App.ApplyPanelStartup</c>) immediately applies the saved configuration with
/// the normal <see cref="PanelConfigApplier"/> once the page, appearance, and monitor selection are
/// ready. Keeping the target inert avoids an early page build or an early panel show while the
/// wizard is still open.
/// </summary>
internal sealed class DeferredPanelConfigApplyTarget : IPanelConfigApplyTarget
{
    /// <inheritdoc />
    public void ApplyWindowOptions(WindowConfig window)
    {
    }

    /// <inheritdoc />
    public void ApplyMonitorTarget(MonitorConfig monitor)
    {
    }

    /// <inheritdoc />
    public void ApplyAppearance(AppearanceConfig appearance)
    {
    }

    /// <inheritdoc />
    public void RebuildActivePage(PanelConfig config)
    {
    }

    /// <inheritdoc />
    public void RebuildActivePage(PanelConfig config, PageTransitionDirection direction)
    {
    }

    /// <inheritdoc />
    public void ApplyTelemetryOptions(TelemetryConfig telemetry)
    {
    }

    /// <inheritdoc />
    public void ReconcileTrackedMetrics(PanelConfig config)
    {
    }

    /// <inheritdoc />
    public void ApplyHotKeys(IReadOnlyList<HotkeyConfig> hotkeys)
    {
    }

    /// <inheritdoc />
    public void ReconcileToggleState(PanelConfig config)
    {
    }
}
