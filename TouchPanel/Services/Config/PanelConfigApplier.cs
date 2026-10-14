using Serilog;
using TouchPanel.Config;

namespace TouchPanel.Services.Config;

/// <summary>
/// The live application seam the shared apply pipeline drives (MD-10, M2-RF-824): window options,
/// active-page rebuild, monitor target/placement policy, global appearance (theme/accent/icon pack),
/// hotkey re-registration, telemetry interval, tracked-metric reconciliation, and toggle-state
/// reconciliation. The composition root implements it and marshals the UI-affine operations to the
/// UI thread; the pipeline itself stays WPF-free and only sequences and isolates the steps.
/// </summary>
public interface IPanelConfigApplyTarget
{
    /// <summary>Applies the window options (live <c>alwaysOnTop</c>) to the running panel.</summary>
    /// <param name="window">The validated window section.</param>
    void ApplyWindowOptions(WindowConfig window);

    /// <summary>Re-evaluates and re-applies the target monitor/placement policy (RF-016/RF-017).</summary>
    /// <param name="monitor">The validated monitor section.</param>
    void ApplyMonitorTarget(MonitorConfig monitor);

    /// <summary>
    /// Applies the global appearance (theme, accent, icon pack) live, with wholesale rollback of the
    /// previous appearance when it fails (M3-RF-848/M3-RF-850). Runs before the page rebuild so
    /// widgets realized afterwards already use the active icon pack.
    /// </summary>
    /// <param name="appearance">The validated appearance section.</param>
    void ApplyAppearance(AppearanceConfig appearance);

    /// <summary>Disposes the current active page view models and rebuilds them from the configuration.</summary>
    /// <param name="config">The validated configuration whose active page is rendered.</param>
    void RebuildActivePage(PanelConfig config);

    /// <summary>Applies the telemetry polling interval (and any capacity reconciliation) live.</summary>
    /// <param name="telemetry">The validated telemetry section.</param>
    void ApplyTelemetryOptions(TelemetryConfig telemetry);

    /// <summary>Reconciles the bounded-history tracked-metric set against the configuration.</summary>
    /// <param name="config">The validated configuration.</param>
    void ReconcileTrackedMetrics(PanelConfig config);

    /// <summary>Unregisters and re-registers the global hotkeys from the configuration.</summary>
    /// <param name="hotkeys">The configured hotkeys.</param>
    void ApplyHotKeys(IReadOnlyList<HotkeyConfig> hotkeys);

    /// <summary>Prunes persisted toggle state that the configuration no longer references.</summary>
    /// <param name="config">The validated configuration.</param>
    void ReconcileToggleState(PanelConfig config);
}

/// <summary>
/// The shared apply pipeline (MD-10, M2-RF-824): applies one fully validated <see cref="PanelConfig"/>
/// to the live application through <see cref="IPanelConfigApplyTarget"/>. Every step is isolated so
/// one failing subsystem cannot prevent the others from applying, and a failure is logged and
/// reported so the workspace stays in a known state. The pipeline never validates and never writes
/// the watched file — invalid candidates are rejected before this is reached (M2-RF-822).
/// </summary>
/// <remarks>
/// The pipeline is deliberately WPF-free: the composition root's target performs any UI-thread
/// marshalling, while telemetry/tracking reconciliation runs on the calling (background) path so the
/// dispatcher is never blocked. The same pipeline is the single source of truth used by hot reload
/// (and, in later tasks, import/settings/page switching).
/// </remarks>
public sealed class PanelConfigApplier
{
    private readonly IPanelConfigApplyTarget _target;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PanelConfigApplier"/> class.
    /// </summary>
    /// <param name="target">The live application seam the steps are applied through.</param>
    /// <param name="logger">Logger for isolated step failures; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <see langword="null"/>.</exception>
    public PanelConfigApplier(IPanelConfigApplyTarget target, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        _target = target;
        _logger = (logger ?? Log.Logger).ForContext<PanelConfigApplier>();
    }

    /// <summary>
    /// Applies one validated configuration to the running application, in a deterministic order:
    /// window options, monitor target, appearance (theme/accent/icon pack), active-page rebuild,
    /// telemetry interval, tracked metrics, hotkeys, and toggle-state reconciliation. Each step is
    /// isolated; a failure is logged and the remaining steps still run, so a single subsystem defect
    /// never strands the panel.
    /// </summary>
    /// <param name="config">The fully validated configuration to apply; must not be <see langword="null"/>.</param>
    /// <returns>The number of steps that failed; <c>0</c> when every step applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public int Apply(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var failures = 0;

        // Order matters: window options and the monitor policy are cheap and make the panel
        // consistent before the heavier page rebuild; the appearance (theme/accent/icon pack) is
        // applied before the rebuild so widgets realized afterwards already use the active pack
        // (M3-RF-848); telemetry/tracking come after so the new widgets can subscribe against the
        // reconciled set.
        failures += Run("window-options", () => _target.ApplyWindowOptions(config.Window));
        failures += Run("monitor-target", () => _target.ApplyMonitorTarget(config.Monitor));
        failures += Run("appearance", () => _target.ApplyAppearance(config.Appearance));
        failures += Run("active-page", () => _target.RebuildActivePage(config));
        failures += Run("telemetry-interval", () => _target.ApplyTelemetryOptions(config.Telemetry));
        failures += Run("tracked-metrics", () => _target.ReconcileTrackedMetrics(config));
        failures += Run("hotkeys", () => _target.ApplyHotKeys(config.Hotkeys));
        failures += Run("toggle-state", () => _target.ReconcileToggleState(config));

        if (failures == 0)
        {
            _logger.Information(
                "Configuration apply: every step applied. Pages={PageCount} Hotkeys={HotkeyCount}",
                config.Pages.Count,
                config.Hotkeys.Count);
        }
        else
        {
            _logger.Warning("Configuration apply: {FailureCount} step(s) failed; the remaining steps still applied", failures);
        }

        return failures;
    }

    /// <summary>
    /// Collects the sensor keys the active configuration page references for bounded history
    /// (gauge and sparkline widgets), in configuration order and without duplicates
    /// (M2-RF-693). Inactive pages are not tracked because no view model consumes their history
    /// until they are shown.
    /// </summary>
    /// <param name="config">The validated configuration; must not be <see langword="null"/>.</param>
    /// <returns>The distinct, non-blank sensor keys referenced by the active page.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> CollectTrackedSensorKeys(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var page = config.Pages.Find(candidate =>
            string.Equals(candidate.Id, config.ActivePageId, StringComparison.Ordinal));
        page ??= config.Pages.Count > 0 ? config.Pages[0] : null;
        if (page is null)
        {
            return [];
        }

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in page.Widgets)
        {
            var sensor = widget switch
            {
                GaugeWidgetConfig gauge => gauge.Sensor,
                SparklineWidgetConfig sparkline => sparkline.Sensor,
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(sensor) && seen.Add(sensor))
            {
                keys.Add(sensor);
            }
        }

        return keys;
    }

    /// <summary>
    /// Computes the history window, in seconds, the shared bounded-history store must retain for the
    /// active configuration page: the maximum <see cref="SparklineWidgetConfig.WindowSeconds"/>
    /// referenced by its sparkline widgets, normalized into the RF-041 range
    /// (<see cref="SparklineWidgetConfig.MinWindowSeconds"/>–<see cref="SparklineWidgetConfig.MaxWindowSeconds"/>).
    /// The store is sized to this maximum so every sparkline sharing it has enough retained samples,
    /// while each widget slices its own window for rendering (M2-RF-691/M2-RF-415). Falls back to
    /// <see cref="SparklineWidgetConfig.DefaultWindowSeconds"/> when the active page has no sparklines
    /// or no pages.
    /// </summary>
    /// <param name="config">The validated configuration; must not be <see langword="null"/>.</param>
    /// <returns>The maximum referenced window, bounded to the spec range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public static double CollectMaxSparklineWindowSeconds(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var page = config.Pages.Find(candidate =>
            string.Equals(candidate.Id, config.ActivePageId, StringComparison.Ordinal));
        page ??= config.Pages.Count > 0 ? config.Pages[0] : null;
        if (page is null)
        {
            return SparklineWidgetConfig.DefaultWindowSeconds;
        }

        var maximum = 0d;
        foreach (var widget in page.Widgets)
        {
            if (widget is not SparklineWidgetConfig sparkline)
            {
                continue;
            }

            var window = double.IsFinite(sparkline.WindowSeconds)
                ? Math.Clamp(
                    sparkline.WindowSeconds,
                    SparklineWidgetConfig.MinWindowSeconds,
                    SparklineWidgetConfig.MaxWindowSeconds)
                : SparklineWidgetConfig.DefaultWindowSeconds;

            if (window > maximum)
            {
                maximum = window;
            }
        }

        return maximum > 0 ? maximum : SparklineWidgetConfig.DefaultWindowSeconds;
    }

    private int Run(string step, Action action)
    {
        try
        {
            action();
            return 0;
        }
        catch (Exception ex)
        {
            // Isolation (RF-012): a failing subsystem must not stop the rest of the apply.
            _logger.Error(ex, "Configuration apply: the {Step} step failed; the running panel may be partially updated", step);
            return 1;
        }
    }
}
