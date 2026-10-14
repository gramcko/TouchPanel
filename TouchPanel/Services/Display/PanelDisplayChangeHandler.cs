using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Health;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;
using TouchPanel.ViewModels;

namespace TouchPanel.Services.Display;

/// <summary>
/// Applies the RF-017/RF-022 policy after an OS display change: it re-evaluates the configured
/// target monitor on demand (never by polling, RNF-008), feeds the fresh selection to
/// <see cref="MainWindowViewModel.ApplyDisplayChange"/>, and executes the returned
/// <see cref="PanelDisplayChangeDecision"/> against the <see cref="IPanelWindow"/> boundary and
/// the <see cref="IUserNotifier"/> (RF-024). Fallback placement is never silent; failures are
/// isolated, logged, and marked in <see cref="SubsystemHealth"/> (RF-012/RNF-013).
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> <see cref="HandleAsync"/> is expected to be called on the UI thread
/// (the <c>DisplayChangeWatcher</c> marshals there). Monitor enumeration runs on the thread pool
/// so a slow driver can never stall the panel (RNF-004); the decision and the window operations
/// resume on the caller's context. Concurrent invocations are serialized so a burst of OS events
/// cannot overlap two native enumerations; the policy itself is state-aware, so repeats are
/// no-ops and notifications are not duplicated.</para>
/// </remarks>
public sealed class PanelDisplayChangeHandler
{
    private readonly MainWindowViewModel _viewModel;
    private readonly MonitorSelectionService _selectionService;
    private readonly Func<PanelConfig?> _configAccessor;
    private readonly IPanelWindow _window;
    private readonly IUserNotifier _notifier;
    private readonly SubsystemHealth? _health;
    private readonly ILogger _logger;
    private readonly Func<PageViewModel?>? _pageAccessor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PanelDisplayChangeHandler"/> class.
    /// </summary>
    /// <param name="viewModel">The panel state/policy owner.</param>
    /// <param name="selectionService">The on-demand monitor selection service (T-07).</param>
    /// <param name="configAccessor">Returns the effective configuration, or <see langword="null"/> until the startup load completed.</param>
    /// <param name="window">The panel window operations boundary (production: <c>MainWindow</c>).</param>
    /// <param name="notifier">The user-notification boundary (RF-010/RF-017).</param>
    /// <param name="health">Optional degraded-subsystem registry (RF-012).</param>
    /// <param name="logger">Logger for decisions and isolated failures; defaults to the global Serilog logger.</param>
    /// <param name="pageAccessor">Returns the rendered page whose widgets are clamped after a re-placement (RF-022), or <see langword="null"/> when no page is rendered yet.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public PanelDisplayChangeHandler(
        MainWindowViewModel viewModel,
        MonitorSelectionService selectionService,
        Func<PanelConfig?> configAccessor,
        IPanelWindow window,
        IUserNotifier notifier,
        SubsystemHealth? health = null,
        ILogger? logger = null,
        Func<PageViewModel?>? pageAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(selectionService);
        ArgumentNullException.ThrowIfNull(configAccessor);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(notifier);

        _viewModel = viewModel;
        _selectionService = selectionService;
        _configAccessor = configAccessor;
        _window = window;
        _notifier = notifier;
        _health = health;
        _logger = (logger ?? Serilog.Log.Logger).ForContext<PanelDisplayChangeHandler>();
        _pageAccessor = pageAccessor;
    }

    /// <summary>
    /// Handles one display-configuration change. Never throws: every failure is logged (and marked
    /// in <see cref="SubsystemHealth"/> when available) so the notification thread and the panel
    /// keep working (RF-012/RNF-013).
    /// </summary>
    /// <returns>A task that completes when the change was classified and applied.</returns>
    public async Task HandleAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            await HandleOnceAsync().ConfigureAwait(true);
            _health?.MarkRestored("display", "display change handled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Display change: handling the display change failed; the panel state is unchanged");
            _health?.MarkDegraded("display", "handling a display change failed", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task HandleOnceAsync()
    {
        var config = _configAccessor();
        if (config is null)
        {
            // A display change can arrive while the startup load is still running; the startup
            // path applies the initial state anyway, so nothing is lost.
            _logger.Debug("Display change: the configuration is not loaded yet; the change was ignored");
            return;
        }

        // Native enumeration stays off the UI thread (RNF-004); the continuation resumes on the
        // caller's context so the window operations below stay on the UI thread.
        var selection = await Task.Run(() => _selectionService.Select(config.Monitor)).ConfigureAwait(true);

        var decision = _viewModel.ApplyDisplayChange(selection);
        Apply(decision);
    }

    private void Apply(PanelDisplayChangeDecision decision)
    {
        switch (decision.Outcome)
        {
            case PanelDisplayChangeOutcome.Show:
                _window.PlaceOnMonitor(decision.Monitor!);
                _window.SetTopmost(_viewModel.AlwaysOnTop);
                _window.ShowWithoutActivation();
                ClampWidgets(decision.Monitor!);
                _logger.Information(
                    "Display change: the configured target monitor returned; the panel was shown without activation. Monitor={Monitor} Reason={Reason}",
                    decision.Monitor!.DisplayName,
                    decision.Reason);
                break;

            case PanelDisplayChangeOutcome.Reposition:
                _window.PlaceOnMonitor(decision.Monitor!);
                ClampWidgets(decision.Monitor!);
                _logger.Information(
                    "Display change: the panel was re-placed. Monitor={Monitor} Fallback={Fallback} Reason={Reason}",
                    decision.Monitor!.DisplayName,
                    decision.UsedFallback,
                    decision.Reason);
                break;

            case PanelDisplayChangeOutcome.Hide:
                _window.HidePanel();
                _logger.Warning("Display change: the panel was hidden. Reason={Reason}", decision.Reason);
                break;

            default:
                _logger.Debug("Display change: no panel action was required. Reason={Reason}", decision.Reason);
                break;
        }

        if (decision.ShouldNotify)
        {
            // RF-017: the user is always told about a fallback placement or a refusal/hide.
            _notifier.Notify(
                decision.NotificationTitle ?? "TouchPanel",
                decision.NotificationMessage!,
                decision.NotificationSeverity);
        }
    }

    /// <summary>
    /// Clamps the rendered widgets into the page bounds of the monitor the panel was just placed
    /// on (RF-022/F-03): a resolution/DPI shrink can otherwise leave oversized or off-page widgets
    /// unreachable on a touchscreen. The DIP page size is the monitor's physical size divided by
    /// the window's DPI scale; the clamp is runtime-only and never writes the configuration, so no
    /// save is triggered and no activation/focus change occurs.
    /// </summary>
    /// <param name="monitor">The monitor the panel was placed on.</param>
    private void ClampWidgets(MonitorDescriptor monitor)
    {
        var page = _pageAccessor?.Invoke();
        if (page is null)
        {
            return;
        }

        var scale = _window.DpiScale;
        if (!double.IsFinite(scale) || scale <= 0)
        {
            scale = 1d;
        }

        var pageWidthDip = monitor.Bounds.Width / scale;
        var pageHeightDip = monitor.Bounds.Height / scale;

        // Keep the edit-mode clamp bounds in sync while it is active, so a later drag cannot move
        // a widget back outside the shrunken page.
        _viewModel.EditModeService?.SetPageBounds(pageWidthDip, pageHeightDip);

        var changed = EditModeService.ClampWidgetsToPage(page.Widgets, pageWidthDip, pageHeightDip);
        if (changed > 0)
        {
            _logger.Information(
                "Display change: widget geometry was clamped into the new page bounds. Monitor={Monitor} PageWidthDip={PageWidthDip} PageHeightDip={PageHeightDip} ChangedWidgets={ChangedWidgets}",
                monitor.DisplayName,
                pageWidthDip,
                pageHeightDip,
                changed);
        }
    }
}
