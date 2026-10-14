using Serilog;
using TouchPanel.Config;

namespace TouchPanel.Services.Config;

/// <summary>
/// Outcome of a page-switch request (T-16/M2-RF-912/M2-RF-914/M2-RF-915): whether the active page
/// actually changed, the resolved page id (after the first-page fallback), whether the selection was
/// persisted, and whether the live page rebuild failed.
/// </summary>
public sealed record PageSwitchResult
{
    /// <summary>Gets a value indicating whether the active page changed (a same-page request is a no-op).</summary>
    public required bool Changed { get; init; }

    /// <summary>Gets the resolved page id after the M1 first-page fallback (RF-037/M2-RF-912).</summary>
    public required string ResolvedPageId { get; init; }

    /// <summary>Gets a value indicating whether the new <c>activePageId</c> was persisted through the store.</summary>
    public bool Persisted { get; init; }

    /// <summary>
    /// Gets a value indicating whether the live page rebuild failed. The panel stays stable without
    /// widgets (M1 <c>AttachActivePage</c> semantics, M2-RF-914); the selection is still applied.
    /// </summary>
    public bool RebuildFailed { get; init; }
}

/// <summary>
/// Boundary a page selector drives to change the active page (T-16). The production implementation
/// is <see cref="PageSwitchCoordinator"/>; view models depend only on this contract so the
/// selection/persistence policy is testable without WPF or the file system (M2-RNF-124).
/// </summary>
public interface IPageSwitchService
{
    /// <summary>
    /// Switches the active page, rebuilds the rendered page, reconciles the tracked-metric set, and
    /// persists the new <c>activePageId</c> atomically through the store.
    /// </summary>
    /// <param name="requestedPageId">The requested page id; an unknown id falls back to the first page (M2-RF-912).</param>
    /// <param name="cancellationToken">Cancels the persistence write.</param>
    /// <returns>The switch outcome; never <see langword="null"/>.</returns>
    Task<PageSwitchResult> SwitchAsync(string requestedPageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Page-switch orchestration (T-16, RF-091, M2-RF-911..915, MD-15). It resolves the requested page
/// with the M1 validation/fallback rules, updates <see cref="PanelConfig.ActivePageId"/>, rebuilds
/// the live page through the shared apply target (which disposes the outgoing page and builds the
/// incoming one, M2-RF-914), reconciles the bounded-history tracked-metric set for the new active
/// page (M2-RF-693), and persists the selection atomically through <see cref="IConfigStore"/> so
/// the app-originated write is suppressed by the hot-reload decision engine (M2-RF-915).
/// </summary>
/// <remarks>
/// The coordinator is WPF-free (MD-10 pattern): the apply target owns UI-thread marshalling. A
/// switch is refused while editing is in progress so a live edit session is never silently
/// discarded, and every failure is isolated so a bad page or store never takes down the panel.
/// </remarks>
public sealed class PageSwitchCoordinator : IPageSwitchService
{
    private readonly Func<PanelConfig?> _configAccessor;
    private readonly IConfigStore _configStore;
    private readonly IPanelConfigApplyTarget _target;
    private readonly Func<bool> _canSwitch;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageSwitchCoordinator"/> class.
    /// </summary>
    /// <param name="configAccessor">Returns the currently running configuration (re-read on every switch so hot reload/import are respected).</param>
    /// <param name="configStore">The single-writer store the selection is persisted through.</param>
    /// <param name="target">The live apply seam that rebuilds the active page and reconciles tracked metrics.</param>
    /// <param name="canSwitch">Guard consulted before switching; defaults to always allowed. The composition root refuses while edit mode is active.</param>
    /// <param name="logger">Logger for switch outcomes; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public PageSwitchCoordinator(
        Func<PanelConfig?> configAccessor,
        IConfigStore configStore,
        IPanelConfigApplyTarget target,
        Func<bool>? canSwitch = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configAccessor);
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentNullException.ThrowIfNull(target);

        _configAccessor = configAccessor;
        _configStore = configStore;
        _target = target;
        _canSwitch = canSwitch ?? (static () => true);
        _logger = (logger ?? Log.Logger).ForContext<PageSwitchCoordinator>();
    }

    /// <inheritdoc />
    public async Task<PageSwitchResult> SwitchAsync(
        string requestedPageId,
        CancellationToken cancellationToken = default)
    {
        var config = _configAccessor();
        if (config is null || config.Pages.Count == 0)
        {
            _logger.Warning("Page switch: no configuration with pages is available; the request was ignored");
            return new PageSwitchResult { Changed = false, ResolvedPageId = requestedPageId };
        }

        var resolved = ResolvePage(config, requestedPageId);
        if (string.Equals(resolved.Id, config.ActivePageId, StringComparison.Ordinal))
        {
            // Already active: nothing is rebuilt or persisted, so a redundant tap can never write
            // panel.json or trigger a reload (M2-RF-915).
            return new PageSwitchResult { Changed = false, ResolvedPageId = resolved.Id };
        }

        if (!_canSwitch())
        {
            _logger.Information(
                "Page switch: switching to {PageId} was refused because an edit session is active",
                resolved.Id);
            return new PageSwitchResult { Changed = false, ResolvedPageId = resolved.Id };
        }

        config.ActivePageId = resolved.Id;

        var rebuildFailed = false;
        try
        {
            // M2-RF-914/M2-RF-824: the target disposes the outgoing page view models (releasing
            // their subscriptions) and builds the incoming page on the UI thread.
            _target.RebuildActivePage(config);
        }
        catch (Exception ex)
        {
            // A rebuild failure leaves the panel without widgets but stable (M1 AttachActivePage).
            rebuildFailed = true;
            _logger.Error(ex, "Page switch: rebuilding page {PageId} failed; the panel stays stable without widgets", resolved.Id);
        }

        try
        {
            // M2-RF-693: reconcile the bounded-history tracked-metric set for the new active page.
            _target.ReconcileTrackedMetrics(config);
        }
        catch (Exception ex)
        {
            // Tracking reconciliation is isolated; a failure degrades history only.
            _logger.Warning(ex, "Page switch: reconciling the tracked metrics for page {PageId} failed", resolved.Id);
        }

        var persisted = await PersistSelectionAsync(config, cancellationToken).ConfigureAwait(false);

        _logger.Information(
            "Page switch: the active page is now {PageId}. Persisted={Persisted} RebuildFailed={RebuildFailed}",
            resolved.Id,
            persisted,
            rebuildFailed);

        return new PageSwitchResult
        {
            Changed = true,
            ResolvedPageId = resolved.Id,
            Persisted = persisted,
            RebuildFailed = rebuildFailed,
        };
    }

    /// <summary>
    /// Resolves the page id the active-page selection should hold: the configured id when it matches
    /// a page, otherwise the first page (M1 RF-037/M2-RF-912). Returns an empty string only when the
    /// configuration has no pages (validation guarantees at least one).
    /// </summary>
    /// <param name="config">The configuration to resolve against; must not be <see langword="null"/>.</param>
    /// <returns>The resolved active page id.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public static string ResolveActivePageId(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Pages.Count == 0)
        {
            return string.Empty;
        }

        var requested = config.ActivePageId;
        var match = string.IsNullOrWhiteSpace(requested)
            ? null
            : config.Pages.Find(page => string.Equals(page.Id, requested, StringComparison.Ordinal));

        return (match ?? config.Pages[0]).Id;
    }

    private async Task<bool> PersistSelectionAsync(PanelConfig config, CancellationToken cancellationToken)
    {
        try
        {
            // The store opens the app self-write window around the atomic replace, so the hot-reload
            // watcher ignores this write (M2-RF-915/M2-RF-823).
            var result = await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return true;
            }

            _logger.Warning("Page switch: the active page selection could not be persisted; the in-memory selection stays active");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Page switch: persisting the active page selection was canceled");
            return false;
        }
        catch (Exception ex)
        {
            // The store contract returns failures; this guards a custom implementation that throws.
            _logger.Warning(ex, "Page switch: persisting the active page selection failed unexpectedly");
            return false;
        }
    }

    private PageConfig ResolvePage(PanelConfig config, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = config.Pages.Find(page => string.Equals(page.Id, requested, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        var fallback = config.Pages[0];
        _logger.Warning(
            "Page switch: page id {RequestedPageId} is not configured; falling back to the first page {FallbackPageId}",
            requested,
            fallback.Id);
        return fallback;
    }
}
