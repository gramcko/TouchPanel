using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Config;

namespace TouchPanel.ViewModels.Settings;

/// <summary>
/// One row of the touch pages editor: the page id, the label to show, whether it is the active
/// page, whether it is the selected row, and its 1-based position in the page order.
/// </summary>
/// <param name="Id">The stable page id used by commands.</param>
/// <param name="DisplayName">The configured name, or the id when no name is set.</param>
/// <param name="IsActive">Whether this page is the active one.</param>
/// <param name="IsSelected">Whether this row is selected for rename/remove.</param>
/// <param name="Position">The 1-based position shown by the editor.</param>
public sealed record PageEditorItem(string Id, string DisplayName, bool IsActive, bool IsSelected, int Position);

/// <summary>
/// Touch-first pages management presentation state (user-requested pages management): list, select,
/// switch, add, rename, remove, and reorder pages (drag or the move buttons). Every structural change
/// is persisted atomically through
/// the shared single-writer settings coordinator (<see cref="ISettingsService"/>), which preserves
/// the other configuration sections and rebuilds the active page through the shared apply pipeline;
/// switching the active page reuses the existing <see cref="IPageSwitchService"/> so the edit-mode
/// refusal and persistence rules stay identical to the in-panel pager.
/// </summary>
/// <remarks>
/// The view model is WPF-free apart from notifications, never mutates the running configuration
/// before a successful save, refuses to remove the last page, and falls back to the first page (with
/// a log entry) when the active page is removed. Reordering preserves the active page by id, keeps
/// every page's widgets and every other section, and is a no-op when the destination is the current
/// position. Add/rename/remove/reorder are disabled while the optional <c>canManage</c> gate reports
/// that edit mode owns the panel, so an edit session is never invalidated by a page-list rebuild.
/// </remarks>
public sealed class PagesEditorViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settings;
    private readonly IPageSwitchService _switchService;
    private readonly Func<PanelConfig?> _configAccessor;
    private readonly Func<bool> _canManage;
    private readonly ILogger _logger;

    private IReadOnlyList<PageEditorItem> _pages = [];
    private string? _selectedPageId;
    private string _selectedPageName = string.Empty;
    private string _newPageName = string.Empty;
    private bool _isBusy;
    private string? _statusMessage;
    private bool _statusIsError;
    private bool _isRemoveConfirmationVisible;
    private string? _pendingRemovePageId;
    private string? _pendingRemovePageName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PagesEditorViewModel"/> class.
    /// </summary>
    /// <param name="settings">The shared single-writer settings boundary; must not be <see langword="null"/>.</param>
    /// <param name="switchService">The existing page-switch boundary used to change the active page; must not be <see langword="null"/>.</param>
    /// <param name="configAccessor">Returns the running configuration (re-read after every apply).</param>
    /// <param name="canManage">Gate consulted before structural changes; defaults to always allowed.</param>
    /// <param name="logger">Logger for page-management summaries; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public PagesEditorViewModel(
        ISettingsService settings,
        IPageSwitchService switchService,
        Func<PanelConfig?> configAccessor,
        Func<bool>? canManage = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(switchService);
        ArgumentNullException.ThrowIfNull(configAccessor);

        _settings = settings;
        _switchService = switchService;
        _configAccessor = configAccessor;
        _canManage = canManage ?? (static () => true);
        _logger = (logger ?? Log.Logger).ForContext<PagesEditorViewModel>();

        AddPageCommand = new AsyncRelayCommand(AddPageAsync);
        RenamePageCommand = new AsyncRelayCommand(RenameSelectedAsync);
        SwitchPageCommand = new AsyncRelayCommand(SwitchToSelectedAsync);
        MovePageUpCommand = new AsyncRelayCommand(MoveSelectedUpAsync);
        MovePageDownCommand = new AsyncRelayCommand(MoveSelectedDownAsync);
        OpenPageCommand = new OpenPageRowCommand(OpenPageAsync);
        RequestRemoveCommand = new RelayCommand(RequestRemoveSelected);
        RequestRemovePageCommand = new SelectPageRowCommand(RequestRemovePage);
        ConfirmRemoveCommand = new AsyncRelayCommand(ConfirmRemoveAsync);
        CancelRemoveCommand = new RelayCommand(CancelRemove);
        SelectPageCommand = new SelectPageRowCommand(SelectPage);

        Refresh();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the configured pages in order, with the active/selected state for the UI.</summary>
    public IReadOnlyList<PageEditorItem> Pages => _pages;

    /// <summary>Gets the selected page id, or <see langword="null"/> when no page is selected.</summary>
    public string? SelectedPageId => _selectedPageId;

    /// <summary>Gets or sets the rename draft for the selected page.</summary>
    public string SelectedPageName
    {
        get => _selectedPageName;
        set
        {
            if (SetProperty(ref _selectedPageName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanRenameSelected));
            }
        }
    }

    /// <summary>Gets or sets the optional name for the next added page; blank means a generated label.</summary>
    public string NewPageName
    {
        get => _newPageName;
        set => SetProperty(ref _newPageName, value ?? string.Empty);
    }

    /// <summary>Gets a value indicating whether structural page changes are currently allowed.</summary>
    public bool CanManagePages => _canManage();

    /// <summary>Gets a value indicating whether a page is selected.</summary>
    public bool HasSelection => _selectedPageId is not null;

    /// <summary>Gets a value indicating whether the selected page can be renamed.</summary>
    public bool CanRenameSelected => CanManagePages && HasSelection && !string.IsNullOrWhiteSpace(_selectedPageName);

    /// <summary>Gets a value indicating whether the selected page can be removed (never the last page).</summary>
    public bool CanRemoveSelected => CanManagePages && HasSelection && _pages.Count > 1;

    /// <summary>Gets a value indicating whether a page can be added.</summary>
    public bool CanAddPage => CanManagePages;

    /// <summary>Gets a value indicating whether the selected page can be made active.</summary>
    public bool CanSwitchToSelected => CanManagePages && HasSelection && SelectedItem?.IsActive == false;

    /// <summary>Gets a value indicating whether the selected page can move one position up.</summary>
    public bool CanMoveSelectedUp => CanManagePages && !IsBusy && SelectedIndex > 0;

    /// <summary>Gets a value indicating whether the selected page can move one position down.</summary>
    public bool CanMoveSelectedDown
    {
        get
        {
            var index = SelectedIndex;
            return CanManagePages && !IsBusy && index >= 0 && index < _pages.Count - 1;
        }
    }

    /// <summary>Gets a value indicating whether the inline remove confirmation is open.</summary>
    public bool IsRemoveConfirmationVisible => _isRemoveConfirmationVisible;

    /// <summary>Gets the page name shown by the remove confirmation.</summary>
    public string PendingRemovePageName => _pendingRemovePageName ?? string.Empty;

    /// <summary>Gets a value indicating whether a page-management operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanMoveSelectedUp));
                OnPropertyChanged(nameof(CanMoveSelectedDown));
            }
        }
    }

    /// <summary>Gets the latest status line, or <see langword="null"/> when idle.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    /// <summary>Gets a value indicating whether a status line is present.</summary>
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    /// <summary>Gets a value indicating whether the latest status line is a failure.</summary>
    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    /// <summary>Gets the command that selects a page row (parameter: page id).</summary>
    public ICommand SelectPageCommand { get; }

    /// <summary>Gets the command that adds a page (using <see cref="NewPageName"/>, or a generated name).</summary>
    public ICommand AddPageCommand { get; }

    /// <summary>Gets the command that renames the selected page to <see cref="SelectedPageName"/>.</summary>
    public ICommand RenamePageCommand { get; }

    /// <summary>Gets the command that makes the selected page active.</summary>
    public ICommand SwitchPageCommand { get; }

    /// <summary>Gets the command that moves the selected page one position up (touch fallback for drag).</summary>
    public ICommand MovePageUpCommand { get; }

    /// <summary>Gets the command that moves the selected page one position down (touch fallback for drag).</summary>
    public ICommand MovePageDownCommand { get; }

    /// <summary>Gets the command that opens (makes active) the page id passed as its parameter.</summary>
    public ICommand OpenPageCommand { get; }

    /// <summary>Gets the command that opens the inline remove confirmation for the selected page.</summary>
    public ICommand RequestRemoveCommand { get; }

    /// <summary>Gets the command that selects the page id passed as its parameter and requests its removal.</summary>
    public ICommand RequestRemovePageCommand { get; }

    /// <summary>Gets the command that confirms removing the pending page.</summary>
    public ICommand ConfirmRemoveCommand { get; }

    /// <summary>Gets the command that cancels the remove confirmation.</summary>
    public ICommand CancelRemoveCommand { get; }

    private PageEditorItem? SelectedItem =>
        _selectedPageId is null
            ? null
            : _pages.FirstOrDefault(item => string.Equals(item.Id, _selectedPageId, StringComparison.Ordinal));

    private int SelectedIndex => IndexOf(_selectedPageId);

    private int IndexOf(string? pageId)
    {
        if (pageId is null)
        {
            return -1;
        }

        for (var index = 0; index < _pages.Count; index++)
        {
            if (string.Equals(_pages[index].Id, pageId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Refreshes the exposed pages, active flag, and selection from the running configuration.
    /// Called at construction, after every successful apply, and after an import/reload. A preferred
    /// page id keeps the selection stable when it still exists.
    /// </summary>
    /// <param name="preferredSelection">The page to select when it exists; otherwise the active page is selected.</param>
    public void Refresh(string? preferredSelection = null)
    {
        var config = _configAccessor();
        if (config is null || config.Pages.Count == 0)
        {
            _pages = [];
            _selectedPageId = null;
            _selectedPageName = string.Empty;
            _isRemoveConfirmationVisible = false;
            _pendingRemovePageId = null;
            _pendingRemovePageName = null;
            RaisePageStateChanged();
            return;
        }

        var activeId = PageSwitchCoordinator.ResolveActivePageId(config);
        _selectedPageId = ResolveSelection(config, preferredSelection, activeId);
        _selectedPageName = ResolveDraftName(config, _selectedPageId);
        _pages = config.Pages
            .Select((page, index) => new PageEditorItem(
                page.Id,
                PageListOperations.DisplayName(page),
                string.Equals(page.Id, activeId, StringComparison.Ordinal),
                string.Equals(page.Id, _selectedPageId, StringComparison.Ordinal),
                index + 1))
            .ToArray();

        if (_pendingRemovePageId is not null
            && !config.Pages.Exists(page => string.Equals(page.Id, _pendingRemovePageId, StringComparison.Ordinal)))
        {
            CancelRemove();
        }

        RaisePageStateChanged();
    }

    private static string? ResolveSelection(PanelConfig config, string? preferredSelection, string activeId)
    {
        if (preferredSelection is not null
            && config.Pages.Exists(page => string.Equals(page.Id, preferredSelection, StringComparison.Ordinal)))
        {
            return preferredSelection;
        }

        return activeId;
    }

    private static string ResolveDraftName(PanelConfig config, string? pageId)
    {
        var page = pageId is null
            ? null
            : config.Pages.Find(candidate => string.Equals(candidate.Id, pageId, StringComparison.Ordinal));
        if (page is null)
        {
            return string.Empty;
        }

        return page.Name ?? PageListOperations.DisplayName(page);
    }

    /// <summary>Selects a page row for rename/remove/switch. Unknown ids are ignored.</summary>
    /// <param name="pageId">The page id to select.</param>
    public void SelectPage(string pageId)
    {
        if (pageId is null || !_pages.Any(item => string.Equals(item.Id, pageId, StringComparison.Ordinal)))
        {
            return;
        }

        _selectedPageId = pageId;
        var config = _configAccessor();
        _selectedPageName = config is null ? string.Empty : ResolveDraftName(config, pageId);
        CancelRemove();
        Refresh(pageId);
    }

    /// <summary>
    /// Adds a page with a unique id and either the requested <see cref="NewPageName"/> or a
    /// generated label. The active page is unchanged. Returns <see langword="false"/> (with a status
    /// line) when edit mode blocks the change or the save fails.
    /// </summary>
    /// <returns><see langword="true"/> when the page was saved and applied.</returns>
    public async Task<bool> AddPageAsync()
    {
        if (!EnsureCanManage())
        {
            return false;
        }

        var config = _configAccessor();
        if (config is null)
        {
            SetStatus("No configuration is available to change.", isError: true);
            return false;
        }

        var add = PageListOperations.AddPage(config.Pages, NewPageName);
        var applied = await ApplyPagesAsync(
            add.Pages,
            PageSwitchCoordinator.ResolveActivePageId(config)).ConfigureAwait(true);
        if (!applied)
        {
            return false;
        }

        NewPageName = string.Empty;
        Refresh(add.AddedPage.Id);
        SetStatus($"Added '{add.AddedPage.Name}'.", isError: false);
        _logger.Information(
            "Pages: added page {PageId} named {PageName}; total={PageCount}",
            add.AddedPage.Id,
            add.AddedPage.Name,
            add.Pages.Count);
        return true;
    }

    /// <summary>
    /// Renames the selected page. Blank names are refused inline (a page always keeps a readable
    /// label); over-long names are truncated by <see cref="PageListOperations.TryNormalizeName"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the rename was saved and applied.</returns>
    public async Task<bool> RenameSelectedAsync()
    {
        if (!EnsureCanManage())
        {
            return false;
        }

        var selected = SelectedItem;
        var config = _configAccessor();
        if (selected is null || config is null)
        {
            SetStatus("Select a page to rename.", isError: true);
            return false;
        }

        if (!PageListOperations.TryNormalizeName(SelectedPageName, out var name))
        {
            SetStatus("Enter a page name.", isError: true);
            return false;
        }

        if (!PageListOperations.TryRenamePage(config.Pages, selected.Id, name, out var pages, out var normalized))
        {
            SetStatus("The selected page no longer exists.", isError: true);
            return false;
        }

        var applied = await ApplyPagesAsync(
            pages,
            PageSwitchCoordinator.ResolveActivePageId(config)).ConfigureAwait(true);
        if (!applied)
        {
            return false;
        }

        Refresh(selected.Id);
        SetStatus($"Renamed the page to '{normalized}'.", isError: false);
        _logger.Information("Pages: renamed page {PageId} to {PageName}", selected.Id, normalized);
        return true;
    }

    /// <summary>Moves the selected page one position up (touch fallback for drag).</summary>
    /// <returns><see langword="true"/> when the order changed and was saved.</returns>
    public Task<bool> MoveSelectedUpAsync()
    {
        var index = SelectedIndex;
        if (!EnsureCanManage())
        {
            return Task.FromResult(false);
        }

        if (index < 0)
        {
            SetStatus("Select a page to move.", isError: true);
            return Task.FromResult(false);
        }

        if (index == 0)
        {
            SetStatus("The page is already first.", isError: false);
            return Task.FromResult(false);
        }

        return MovePageAsync(_pages[index].Id, index - 1);
    }

    /// <summary>Moves the selected page one position down (touch fallback for drag).</summary>
    /// <returns><see langword="true"/> when the order changed and was saved.</returns>
    public Task<bool> MoveSelectedDownAsync()
    {
        var index = SelectedIndex;
        if (!EnsureCanManage())
        {
            return Task.FromResult(false);
        }

        if (index < 0)
        {
            SetStatus("Select a page to move.", isError: true);
            return Task.FromResult(false);
        }

        if (index >= _pages.Count - 1)
        {
            SetStatus("The page is already last.", isError: false);
            return Task.FromResult(false);
        }

        return MovePageAsync(_pages[index].Id, index + 1);
    }

    /// <summary>
    /// Moves a page to <paramref name="targetIndex"/> and persists the new order through the shared
    /// single writer. Only the page order changes: every page (with its widgets), every other section,
    /// and the active page id are preserved, so the page that was active stays active by id even when
    /// its index changes. A drop on the current position, an unknown page, or a single-page list is a
    /// no-op that saves nothing. The last-page guard, edit-mode gate, and fallback stay in force.
    /// </summary>
    /// <param name="pageId">The id of the page to move.</param>
    /// <param name="targetIndex">The desired 0-based destination index; clamped to the list bounds.</param>
    /// <returns><see langword="true"/> when the order changed and was saved.</returns>
    public async Task<bool> MovePageAsync(string pageId, int targetIndex)
    {
        if (!EnsureCanManage())
        {
            return false;
        }

        if (IsBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(pageId))
        {
            SetStatus("Select a page to move.", isError: true);
            return false;
        }

        var config = _configAccessor();
        if (config is null)
        {
            SetStatus("No configuration is available to change.", isError: true);
            return false;
        }

        var move = PageListOperations.MovePage(
            config.Pages,
            pageId,
            targetIndex,
            PageSwitchCoordinator.ResolveActivePageId(config));

        if (!move.Moved)
        {
            if (move.FailureReason is not null)
            {
                SetStatus(move.FailureReason, isError: true);
            }
            else
            {
                SetStatus("The page is already in that position.", isError: false);
            }

            return false;
        }

        var applied = await ApplyPagesAsync(move.Pages, move.ActivePageId).ConfigureAwait(true);
        if (!applied)
        {
            return false;
        }

        Refresh(pageId);
        var label = move.ToIndex >= 0 && move.ToIndex < move.Pages.Count
            ? PageListOperations.DisplayName(move.Pages[move.ToIndex])
            : pageId;
        SetStatus($"Moved '{label}' to position {move.ToIndex + 1}.", isError: false);
        _logger.Information(
            "Pages: moved page {PageId} from position {FromPosition} to {ToPosition}; active page stays {ActivePageId}",
            pageId,
            move.FromIndex + 1,
            move.ToIndex + 1,
            move.ActivePageId);
        return true;
    }

    /// <summary>
    /// Selects the supplied page row and opens the inline remove confirmation for it. The last page
    /// is never removed; the call is refused with a status line instead.
    /// </summary>
    /// <param name="pageId">The page id whose row requested removal.</param>
    public void RequestRemovePage(string pageId)
    {
        if (!CanManagePages)
        {
            SetStatus("Page changes are unavailable while edit mode is active.", isError: true);
            return;
        }

        SelectPage(pageId);
        RequestRemoveSelected();
    }

    /// <summary>Opens the inline remove confirmation for the selected page (never the last page).</summary>
    public void RequestRemoveSelected()
    {
        if (!CanRemoveSelected || SelectedItem is not { } selected)
        {
            SetStatus(_pages.Count <= 1
                ? "The last page cannot be removed."
                : "Select a page to remove.", isError: true);
            return;
        }

        _pendingRemovePageId = selected.Id;
        _pendingRemovePageName = selected.DisplayName;
        SetRemoveConfirmationVisible(true);
        OnPropertyChanged(nameof(PendingRemovePageName));
    }

    /// <summary>
    /// Confirms the open remove confirmation. Removing the active page falls back deterministically
    /// to the first remaining page and logs the fallback; removing the last page is refused even if
    /// the confirmation was somehow opened. The page list and active id are persisted together
    /// through the single writer.
    /// </summary>
    /// <returns><see langword="true"/> when a page was removed and saved.</returns>
    public async Task<bool> ConfirmRemoveAsync()
    {
        if (!_isRemoveConfirmationVisible)
        {
            return false;
        }

        if (!EnsureCanManage())
        {
            CancelRemove();
            return false;
        }

        var pendingId = _pendingRemovePageId;
        if (pendingId is null)
        {
            CancelRemove();
            return false;
        }

        var config = _configAccessor();
        if (config is null)
        {
            CancelRemove();
            SetStatus("No configuration is available to change.", isError: true);
            return false;
        }

        var removal = PageListOperations.RemovePage(config.Pages, pendingId, config.ActivePageId);
        if (!removal.Removed)
        {
            CancelRemove();
            SetStatus(removal.FailureReason ?? "The page could not be removed.", isError: true);
            return false;
        }

        var applied = await ApplyPagesAsync(removal.Pages, removal.ActivePageId).ConfigureAwait(true);
        if (!applied)
        {
            return false;
        }

        var preferred = removal.ActivePageFellBack ? removal.ActivePageId : _selectedPageId;
        CancelRemove();
        Refresh(preferred);
        SetStatus(
            removal.ActivePageFellBack
                ? $"Removed the active page; '{removal.ActivePageId}' is now active."
                : "Removed the page.",
            isError: false);

        if (removal.ActivePageFellBack)
        {
            _logger.Information(
                "Pages: the active page {PageId} was removed; the active page fell back to the first remaining page {FallbackPageId}",
                pendingId,
                removal.ActivePageId);
        }
        else
        {
            _logger.Information("Pages: removed page {PageId}; total={PageCount}", pendingId, removal.Pages.Count);
        }

        return true;
    }

    /// <summary>Switches the active page to the selected row through the existing page-switch service.</summary>
    /// <returns><see langword="true"/> when the active page changed.</returns>
    public Task<bool> SwitchToSelectedAsync() =>
        SelectedItem is { } selected
            ? OpenPageAsync(selected.Id)
            : RefuseSelectedPage();

    /// <summary>
    /// Makes the supplied page active through the existing page-switch service (the same single
    /// writer, fallback, rebuild, and edit-mode refusal used by the in-panel pager).
    /// </summary>
    /// <param name="pageId">The page id to open.</param>
    /// <returns><see langword="true"/> when the active page changed.</returns>
    public async Task<bool> OpenPageAsync(string pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            SetStatus("Select a page to open.", isError: true);
            return false;
        }

        PageSwitchResult result;
        try
        {
            result = await _switchService.SwitchAsync(pageId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Pages: switching to page {PageId} failed unexpectedly", pageId);
            SetStatus("The page could not be switched.", isError: true);
            return false;
        }

        if (!result.Changed)
        {
            var currentConfig = _configAccessor();
            var alreadyActive = currentConfig is not null
                && string.Equals(
                    result.ResolvedPageId,
                    PageSwitchCoordinator.ResolveActivePageId(currentConfig),
                    StringComparison.Ordinal);
            SetStatus(
                CanManagePages
                    ? alreadyActive
                        ? "That page is already active."
                        : "The page could not be opened."
                    : "Page switching is unavailable while edit mode is active.",
                isError: true);
            return false;
        }

        Refresh(result.ResolvedPageId);
        var config = _configAccessor();
        var page = config?.Pages.Find(candidate =>
            string.Equals(candidate.Id, result.ResolvedPageId, StringComparison.Ordinal));
        var label = page is null ? result.ResolvedPageId : PageListOperations.DisplayName(page);
        SetStatus($"Switched to '{label}'.", isError: false);
        return true;
    }

    private Task<bool> RefuseSelectedPage()
    {
        SetStatus("Select a page to open.", isError: true);
        return Task.FromResult(false);
    }

    /// <summary>Closes the remove confirmation without changing anything.</summary>
    public void CancelRemove()
    {
        _pendingRemovePageId = null;
        _pendingRemovePageName = null;
        SetRemoveConfirmationVisible(false);
        OnPropertyChanged(nameof(PendingRemovePageName));
    }

    private async Task<bool> ApplyPagesAsync(IReadOnlyList<PageConfig> pages, string activePageId)
    {
        IsBusy = true;
        try
        {
            // The shared settings coordinator serializes the read-build-save-apply sequence, validates
            // the candidate, preserves every other section, and publishes/applies only after the
            // atomic save succeeded (single writer).
            var result = await _settings.ApplyAsync(new SettingsChange
            {
                Pages = pages,
                ActivePageId = activePageId,
            }).ConfigureAwait(true);

            if (!result.Succeeded)
            {
                SetStatus(result.ErrorMessage ?? "The page change could not be saved.", isError: true);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Pages: applying a page change failed unexpectedly");
            SetStatus("The page change could not be applied.", isError: true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool EnsureCanManage()
    {
        if (CanManagePages)
        {
            return true;
        }

        SetStatus("Page changes are unavailable while edit mode is active.", isError: true);
        return false;
    }

    private void SetRemoveConfirmationVisible(bool visible)
    {
        if (_isRemoveConfirmationVisible == visible)
        {
            return;
        }

        _isRemoveConfirmationVisible = visible;
        OnPropertyChanged(nameof(IsRemoveConfirmationVisible));
    }

    private void SetStatus(string? message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    private void RaisePageStateChanged()
    {
        OnPropertyChanged(nameof(Pages));
        OnPropertyChanged(nameof(SelectedPageId));
        OnPropertyChanged(nameof(SelectedPageName));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanManagePages));
        OnPropertyChanged(nameof(CanRenameSelected));
        OnPropertyChanged(nameof(CanRemoveSelected));
        OnPropertyChanged(nameof(CanAddPage));
        OnPropertyChanged(nameof(CanSwitchToSelected));
        OnPropertyChanged(nameof(CanMoveSelectedUp));
        OnPropertyChanged(nameof(CanMoveSelectedDown));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Parameterized command that opens the page id passed by a row, without requiring the row to be
    /// selected first. Failures are contained; the view model already turns them into a status line.
    /// </summary>
    private sealed class OpenPageRowCommand : ICommand
    {
        private readonly Func<string, Task> _execute;

        public OpenPageRowCommand(Func<string, Task> execute) => _execute = execute;

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
                // Keep the dispatcher alive; the view model contains/logs operation failures.
            }
        }
    }

    /// <summary>
    /// Parameterized command for selecting a page row. Kept as a thin variant of the pager's command
    /// so the row can pass its id without a per-row code-behind.
    /// </summary>
    private sealed class SelectPageRowCommand : ICommand
    {
        private readonly Action<string> _execute;

        public SelectPageRowCommand(Action<string> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is string;

        public void Execute(object? parameter)
        {
            if (parameter is string pageId)
            {
                _execute(pageId);
            }
        }
    }
}
