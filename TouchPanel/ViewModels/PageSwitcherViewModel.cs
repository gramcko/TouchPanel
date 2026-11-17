using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// One entry of the in-panel page selector (RF-091/M2-RF-911): the page id, the touch label to show,
/// and whether it is the active page (drives the accent highlight without a converter).
/// </summary>
/// <param name="Id">The page id used as the switch command parameter and matched against <c>activePageId</c>.</param>
/// <param name="DisplayName">The touch label; pages carry no title, so the id is shown.</param>
/// <param name="IsActive">Whether this page is the currently active one.</param>
public sealed record PageSwitcherItem(string Id, string DisplayName, bool IsActive);

/// <summary>
/// Presentation state for the in-panel page selector (T-16, RF-091, M2-RF-911..915): the configured
/// pages, the active page, and the touch switch command. The actual switch (fallback resolution,
/// page rebuild, tracked-metric reconciliation, and atomic persistence) is owned by
/// <see cref="IPageSwitchService"/>; the view model only mirrors the outcome so it stays WPF-free
/// and testable (M2-RNF-124). Non-activating by construction: the selector binds non-focusable
/// controls and never calls <c>Activate</c>/<c>SetForegroundWindow</c> (M2-RF-913).
/// </summary>
public sealed class PageSwitcherViewModel : INotifyPropertyChanged
{
    private readonly IPageSwitchService _switchService;
    private readonly ILogger _logger;
    private PanelConfig _config;
    private IReadOnlyList<PageSwitcherItem> _pages;
    private string _activePageId;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageSwitcherViewModel"/> class.
    /// </summary>
    /// <param name="config">The validated configuration whose pages are offered; must not be <see langword="null"/>.</param>
    /// <param name="switchService">The page-switch boundary that owns rebuild and persistence; must not be <see langword="null"/>.</param>
    /// <param name="logger">Logger for switch outcomes; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public PageSwitcherViewModel(
        PanelConfig config,
        IPageSwitchService switchService,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(switchService);

        _config = config;
        _switchService = switchService;
        _logger = (logger ?? Log.Logger).ForContext<PageSwitcherViewModel>();
        _activePageId = PageSwitchCoordinator.ResolveActivePageId(config);
        _pages = BuildPages();
        SelectPageCommand = new SwitchPageCommand(pageId => SwitchToAsync(pageId));
        NextPageCommand = new AsyncRelayCommand(() => MoveByAsync(1));
        PreviousPageCommand = new AsyncRelayCommand(() => MoveByAsync(-1));
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the configured pages in configuration order (M2-RF-911).</summary>
    public IReadOnlyList<PageSwitcherItem> Pages => _pages;

    /// <summary>Gets the currently active page id (the resolved, persisted <c>activePageId</c>).</summary>
    public string ActivePageId => _activePageId;

    /// <summary>
    /// Gets a value indicating whether more than one page is configured. A single-page panel renders
    /// no selector (there is nothing to switch to); the host collapses on this flag.
    /// </summary>
    public bool CanSwitch => _pages.Count > 1;

    /// <summary>Gets the zero-based index of the active page, or <c>-1</c> when no page is active.</summary>
    public int ActiveIndex
    {
        get
        {
            for (var index = 0; index < _pages.Count; index++)
            {
                if (string.Equals(_pages[index].Id, _activePageId, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }
    }

    /// <summary>Gets the number of configured pages shown by the pager indicator.</summary>
    public int PageCount => _pages.Count;

    /// <summary>
    /// Gets the human-readable position indicator (for example <c>2 / 3</c>) shown by the pager in
    /// addition to the dots, so the current page and the count are visible without hover.
    /// </summary>
    public string PageIndicatorText =>
        _pages.Count == 0 || ActiveIndex < 0
            ? string.Empty
            : $"{ActiveIndex + 1} / {_pages.Count}";

    /// <summary>Gets a value indicating whether a later page exists (the pager does not wrap at the end).</summary>
    public bool CanGoNext => CanSwitch && ActiveIndex >= 0 && ActiveIndex < _pages.Count - 1;

    /// <summary>Gets a value indicating whether an earlier page exists.</summary>
    public bool CanGoPrevious => CanSwitch && ActiveIndex > 0;

    /// <summary>Gets the touch command that switches to the page id passed as its parameter.</summary>
    public ICommand SelectPageCommand { get; }

    /// <summary>Gets the command that advances one page, used by the pager's next affordance.</summary>
    public ICommand NextPageCommand { get; }

    /// <summary>Gets the command that moves back one page, used by the pager's previous affordance.</summary>
    public ICommand PreviousPageCommand { get; }

    /// <summary>
    /// Switches to the requested page through <see cref="IPageSwitchService"/> and mirrors the
    /// resolved selection. Unknown ids are resolved to the first page by the service (M2-RF-912); a
    /// redundant tap on the active page is a no-op. Failures are contained and reported as
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="pageId">The requested page id; must not be <see langword="null"/>.</param>
    /// <param name="direction">
    /// The page-change direction used by the normal-mode transition (user-requested addition). Tabs,
    /// Open, and direct selections use the default forward direction.
    /// </param>
    /// <returns><see langword="true"/> when the active page changed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pageId"/> is <see langword="null"/>.</exception>
    public async Task<bool> SwitchToAsync(
        string pageId,
        PageTransitionDirection direction = PageTransitionDirection.Forward)
    {
        ArgumentNullException.ThrowIfNull(pageId);

        PageSwitchResult result;
        try
        {
            result = await _switchService.SwitchAsync(pageId, direction).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // M2-RF-914/RF-012: a switch failure must not take down the selector or the panel.
            _logger.Warning(ex, "Page switch: the request for page {PageId} failed unexpectedly", pageId);
            return false;
        }

        if (!result.Changed)
        {
            return false;
        }

        _activePageId = result.ResolvedPageId;
        _pages = BuildPages();
        RaisePresentationChanged();
        return true;
    }

    /// <summary>
    /// Moves one page forward (<paramref name="direction"/> &gt; 0) or back
    /// (<paramref name="direction"/> &lt; 0), clamped at the ends (the pager does not wrap). Used by
    /// the touch/mouse swipe recognizer and by the pager's next/previous affordances; the actual
    /// rebuild and persistence stay in <see cref="IPageSwitchService"/>. Unknown, zero, or
    /// out-of-range requests are no-ops.
    /// </summary>
    /// <param name="direction">The relative page step; only the sign is used.</param>
    /// <returns><see langword="true"/> when the active page changed; otherwise <see langword="false"/>.</returns>
    public async Task<bool> MoveByAsync(int direction)
    {
        var step = Math.Sign(direction);
        if (step == 0)
        {
            return false;
        }

        var index = ActiveIndex;
        if (index < 0)
        {
            return false;
        }

        var target = index + step;
        if (target < 0 || target >= _pages.Count)
        {
            return false;
        }

        // A swipe/drag to the left (next) moves forward, so the new page enters from the right; a
        // swipe to the right (previous) moves backward, so it enters from the left (direction-aware
        // slide rotation, user-requested addition).
        var transitionDirection = step > 0 ? PageTransitionDirection.Forward : PageTransitionDirection.Backward;
        return await SwitchToAsync(_pages[target].Id, transitionDirection).ConfigureAwait(true);
    }

    /// <summary>
    /// Refreshes the offered pages and the active id from a new effective configuration after a hot
    /// reload or import (M2-RF-824/M2-RF-912). The switch service reads the live configuration itself,
    /// so only the mirrored presentation state is rebuilt here.
    /// </summary>
    /// <param name="config">The new effective configuration; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public void ApplyConfiguration(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _activePageId = PageSwitchCoordinator.ResolveActivePageId(config);
        _pages = BuildPages();
        RaisePresentationChanged();
    }

    private IReadOnlyList<PageSwitcherItem> BuildPages() =>
        _config.Pages
            .Select(page => new PageSwitcherItem(
                page.Id,
                PageListOperations.DisplayName(page),
                string.Equals(page.Id, _activePageId, StringComparison.Ordinal)))
            .ToArray();

    private void RaisePresentationChanged()
    {
        OnPropertyChanged(nameof(ActivePageId));
        OnPropertyChanged(nameof(Pages));
        OnPropertyChanged(nameof(CanSwitch));
        OnPropertyChanged(nameof(ActiveIndex));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageIndicatorText));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoPrevious));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Parameterized asynchronous command for the page tabs. Widgets use the non-generic
    /// <see cref="AsyncRelayCommand"/>; the selector needs the tapped page id, so this thin variant
    /// carries a <see cref="string"/> parameter and contains every failure (RF-050).
    /// </summary>
    private sealed class SwitchPageCommand : ICommand
    {
        private readonly Func<string, Task> _execute;

        public SwitchPageCommand(Func<string, Task> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is string;

        public async void Execute(object? parameter)
        {
            if (parameter is not string pageId)
            {
                return;
            }

            try
            {
                await _execute(pageId).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Never surface an exception to the dispatcher; the service and view model already
                // contain and log failures.
            }
        }
    }
}
