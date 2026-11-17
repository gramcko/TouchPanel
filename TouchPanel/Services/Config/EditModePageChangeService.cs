using Serilog;

namespace TouchPanel.Services.Config;

/// <summary>
/// Boundary a pages surface uses to temporarily step out of an active edit session before a
/// structural page change (which rebuilds the live page), and to step back in afterwards. The
/// production implementation is the composition root, which reuses the same save-on-exit and
/// deliberate re-entry paths as Esc and the tray toggle.
/// </summary>
public interface IEditSessionGate
{
    /// <summary>Gets a value indicating whether edit mode currently owns the panel.</summary>
    bool IsEditModeActive { get; }

    /// <summary>
    /// Saves pending layout changes and exits edit mode so a page rebuild cannot discard them.
    /// Returns <see langword="false"/> when the pending layout could not be saved, in which case the
    /// caller must abort the page change and edit mode stays active.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save/exit.</param>
    /// <returns><see langword="true"/> when it is safe to change pages; otherwise <see langword="false"/>.</returns>
    Task<bool> SuspendAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enters edit mode after the page change through the composition root's deliberate entry
    /// path (which may activate, D-6). Safe to call when the panel is no longer available.
    /// </summary>
    /// <returns>A task that completes when the re-entry request has been handled.</returns>
    Task ResumeAsync();
}

/// <summary>
/// Delegate-based <see cref="IEditSessionGate"/> used by the composition root. Keeping the gate a
/// plain callback boundary lets the page-management flow be unit-tested without WPF or a window.
/// </summary>
public sealed class DelegateEditSessionGate : IEditSessionGate
{
    private readonly Func<bool> _isActive;
    private readonly Func<CancellationToken, Task<bool>> _suspend;
    private readonly Func<Task> _resume;

    /// <summary>Initializes a new instance of the <see cref="DelegateEditSessionGate"/> class.</summary>
    /// <param name="isActive">Reports whether edit mode is active.</param>
    /// <param name="suspend">Saves and exits edit mode; returns <see langword="false"/> to abort.</param>
    /// <param name="resume">Re-enters edit mode.</param>
    /// <exception cref="ArgumentNullException">Thrown when a callback is <see langword="null"/>.</exception>
    public DelegateEditSessionGate(
        Func<bool> isActive,
        Func<CancellationToken, Task<bool>> suspend,
        Func<Task> resume)
    {
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        _suspend = suspend ?? throw new ArgumentNullException(nameof(suspend));
        _resume = resume ?? throw new ArgumentNullException(nameof(resume));
    }

    /// <inheritdoc />
    public bool IsEditModeActive => _isActive();

    /// <inheritdoc />
    public Task<bool> SuspendAsync(CancellationToken cancellationToken = default) =>
        _suspend(cancellationToken);

    /// <inheritdoc />
    public Task ResumeAsync() => _resume();
}

/// <summary>
/// Edit-mode page-management adapter. It presents the shared single-writer settings boundary and the
/// existing page-switch coordinator to the edit-mode pages surface, but steps out of the edit
/// session around any operation that rebuilds the active page: a structural change
/// (add/rename/remove) or opening another page. The edit session is then re-entered on the resulting
/// page, so the panel stays in edit mode and pending widget edits are saved first. Outside edit mode
/// every call is a direct pass-through, so the Settings pages editor behaves exactly as before.
/// </summary>
/// <remarks>
/// Persistence and the page-list guard rules remain owned by <see cref="ISettingsService"/> and
/// <see cref="PageListOperations"/>; this adapter only sequences the suspension/re-entry and can
/// never write configuration itself. If pending layout changes cannot be saved, the operation is
/// refused with a stable <c>pages.unsaved</c> failure and nothing is changed.
/// </remarks>
public sealed class EditModePageChangeService : ISettingsService, IPageSwitchService
{
    private readonly ISettingsService _settings;
    private readonly IPageSwitchService _pageSwitch;
    private readonly IEditSessionGate _session;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditModePageChangeService"/> class.
    /// </summary>
    /// <param name="settings">The shared single-writer settings boundary; must not be <see langword="null"/>.</param>
    /// <param name="pageSwitch">The existing page-switch coordinator; must not be <see langword="null"/>.</param>
    /// <param name="session">The edit-session suspend/resume boundary; must not be <see langword="null"/>.</param>
    /// <param name="logger">Logger for page-change summaries; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public EditModePageChangeService(
        ISettingsService settings,
        IPageSwitchService pageSwitch,
        IEditSessionGate session,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(pageSwitch);
        ArgumentNullException.ThrowIfNull(session);

        _settings = settings;
        _pageSwitch = pageSwitch;
        _session = session;
        _logger = (logger ?? Log.Logger).ForContext<EditModePageChangeService>();
    }

    /// <inheritdoc />
    public async Task<SettingsApplyResult> ApplyAsync(
        SettingsChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!_session.IsEditModeActive)
        {
            return await _settings.ApplyAsync(change, cancellationToken).ConfigureAwait(true);
        }

        if (!await _session.SuspendAsync(cancellationToken).ConfigureAwait(true))
        {
            return SettingsApplyResult.Failure(
                "pages.unsaved",
                "Pending layout changes could not be saved; the page change was canceled.");
        }

        try
        {
            // The shared coordinator validates, persists through the single-writer store, preserves
            // every other section, and rebuilds/applies the resulting active page.
            return await _settings.ApplyAsync(change, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            await _session.ResumeAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task<PageSwitchResult> SwitchAsync(
        string requestedPageId,
        CancellationToken cancellationToken = default)
    {
        if (!_session.IsEditModeActive)
        {
            return await _pageSwitch.SwitchAsync(requestedPageId, cancellationToken).ConfigureAwait(true);
        }

        if (!await _session.SuspendAsync(cancellationToken).ConfigureAwait(true))
        {
            _logger.Warning("Pages: opening {PageId} was refused because the pending layout could not be saved", requestedPageId);
            return new PageSwitchResult { Changed = false, ResolvedPageId = requestedPageId };
        }

        try
        {
            return await _pageSwitch.SwitchAsync(requestedPageId, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            await _session.ResumeAsync().ConfigureAwait(true);
        }
    }
}
