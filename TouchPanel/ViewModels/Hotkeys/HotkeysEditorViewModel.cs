using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Input;
using TouchPanel.Services.Config;
using TouchPanel.Services.Notifications;

namespace TouchPanel.ViewModels.Hotkeys;

/// <summary>
/// Presentation state for the focused, touch-first hotkey editor (RF-061/M3-RF-611..619). It is a
/// thin view model over the WPF-free <see cref="HotkeyEditorModel"/>: it binds the list and the
/// add/edit/remove draft (modifier toggles + a friendly-key selector + the action editor), surfaces
/// inline validation and duplicate feedback, and persists through the single-writer
/// <see cref="ISettingsService"/> path. Saving triggers the shared apply pipeline, which re-registers
/// the hotkeys live through <c>IPanelConfigApplyTarget.ApplyHotKeys</c> (M3-RF-614) with no
/// restart and no activation.
/// </summary>
/// <remarks>
/// <para><b>Failure isolation (M3-RF-617/M3-RNF-138).</b> A save failure keeps the dirty edits in
/// the editor, leaves the running configuration and the currently registered hotkeys intact (the
/// apply hook is never reached), and is surfaced as a status line. A failure after the save is
/// contained by the shared pipeline and reported by the hotkey service's single aggregated
/// notification (M3-RF-618).</para>
/// <para><b>Service-unavailable path (M3-RF-611).</b> When the hotkey service failed to initialize
/// the editor still opens, shows the configured list, and displays an "unavailable this session"
/// note; saving still persists the configuration.</para>
/// </remarks>
public sealed class HotkeysEditorViewModel : INotifyPropertyChanged
{
    private readonly HotkeyEditorModel _model;
    private readonly ISettingsService _settings;
    private readonly IUserNotifier _notifier;
    private readonly ILogger _logger;
    private readonly bool _hotkeysAvailable;
    private readonly Func<IReadOnlyList<HotkeyConfig>>? _reloadSource;

    private HotkeyEditorEntry? _selectedEntry;
    private bool _modifierCtrl;
    private bool _modifierAlt;
    private bool _modifierShift;
    private bool _modifierWin;
    private string _selectedKey = "A";
    private bool _isBusy;
    private string? _statusMessage;
    private bool _statusIsError;
    private string? _validationError;
    private bool _suppressSelectionReload;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeysEditorViewModel"/> class.
    /// </summary>
    /// <param name="model">The WPF-free editing model seeded from the effective configuration; must not be <see langword="null"/>.</param>
    /// <param name="settings">The single-writer apply boundary; must not be <see langword="null"/>.</param>
    /// <param name="notifier">The RF-010 notification boundary used for the service-unavailable note; must not be <see langword="null"/>.</param>
    /// <param name="hotkeysAvailable">Whether the global hotkey service is available in this session.</param>
    /// <param name="reloadSource">Optional accessor returning the currently running hotkeys, used by <see cref="ReloadFromModel"/>.</param>
    /// <param name="logger">Logger for summary-only messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public HotkeysEditorViewModel(
        HotkeyEditorModel model,
        ISettingsService settings,
        IUserNotifier notifier,
        bool hotkeysAvailable,
        Func<IReadOnlyList<HotkeyConfig>>? reloadSource = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notifier);

        _model = model;
        _settings = settings;
        _notifier = notifier;
        _hotkeysAvailable = hotkeysAvailable;
        _reloadSource = reloadSource;
        _logger = (logger ?? Log.Logger).ForContext<HotkeysEditorViewModel>();

        KeyOptions = SendKeyCombination.FriendlyKeyNames;
        ModifierOptions = SendKeyCombination.KnownModifierNames;
        ActionEditor = new HotkeyActionEditorViewModel();

        NewCommand = new RelayCommand(NewDraft);
        SaveEntryCommand = new RelayCommand(() => SaveEntry());
        RemoveCommand = new RelayCommand(() => RemoveSelected());
        ApplyCommand = new AsyncRelayCommand(ApplyAsync);
        ReloadCommand = new RelayCommand(ReloadFromModel);

        ResetDraft();
        RebuildEntries(selectId: null);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the editable entries as an observable collection for the list.</summary>
    public ObservableCollection<HotkeyEditorEntry> Entries { get; } = [];

    /// <summary>Gets the friendly key catalog (shared with the runtime mapping, M3-RF-612).</summary>
    public IReadOnlyList<string> KeyOptions { get; }

    /// <summary>Gets the four modifier names in canonical order (M3-RF-612).</summary>
    public IReadOnlyList<string> ModifierOptions { get; }

    /// <summary>Gets the action editor for the current draft (M3-RF-612).</summary>
    public HotkeyActionEditorViewModel ActionEditor { get; }

    /// <summary>Gets a value indicating whether the global hotkey service is available in this session.</summary>
    public bool HotkeysAvailable => _hotkeysAvailable;

    /// <summary>Gets the "hotkeys unavailable this session" note, or <see langword="null"/> when the service is available.</summary>
    public string? UnavailableNote => _hotkeysAvailable
        ? null
        : "Global hotkeys are unavailable in this session. You can still edit and save the list; the changes take effect on the next start.";

    /// <summary>Gets a value indicating whether the unavailable note is shown.</summary>
    public bool HasUnavailableNote => !_hotkeysAvailable;

    /// <summary>Gets the currently selected entry, or <see langword="null"/> when a new entry is being drafted.</summary>
    public HotkeyEditorEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (ReferenceEquals(_selectedEntry, value))
            {
                return;
            }

            _selectedEntry = value;
            OnPropertyChanged();

            if (!_suppressSelectionReload && value is { } entry)
            {
                LoadDraftFrom(entry);
            }
        }
    }

    /// <summary>Gets or sets whether the <c>Ctrl</c> modifier is part of the draft.</summary>
    public bool ModifierCtrl
    {
        get => _modifierCtrl;
        set => SetField(ref _modifierCtrl, value);
    }

    /// <summary>Gets or sets whether the <c>Alt</c> modifier is part of the draft.</summary>
    public bool ModifierAlt
    {
        get => _modifierAlt;
        set => SetField(ref _modifierAlt, value);
    }

    /// <summary>Gets or sets whether the <c>Shift</c> modifier is part of the draft.</summary>
    public bool ModifierShift
    {
        get => _modifierShift;
        set => SetField(ref _modifierShift, value);
    }

    /// <summary>Gets or sets whether the <c>Win</c> modifier is part of the draft.</summary>
    public bool ModifierWin
    {
        get => _modifierWin;
        set => SetField(ref _modifierWin, value);
    }

    /// <summary>Gets or sets the draft friendly key name.</summary>
    public string SelectedKey
    {
        get => _selectedKey;
        set => SetField(ref _selectedKey, value ?? string.Empty);
    }

    /// <summary>Gets a value indicating whether the working copy has unsaved changes.</summary>
    public bool IsDirty => _model.IsDirty;

    /// <summary>Gets a value indicating whether the apply action is available (valid and dirty).</summary>
    public bool CanApply => _model.IsValid && _model.IsDirty;

    /// <summary>Gets a value indicating whether an operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>Gets the latest status line (success or failure), or <see langword="null"/> when idle.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
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
        private set => SetField(ref _statusIsError, value);
    }

    /// <summary>Gets the inline validation message, or <see langword="null"/> when the draft is acceptable.</summary>
    public string? ValidationError
    {
        get => _validationError;
        private set
        {
            if (SetField(ref _validationError, value))
            {
                OnPropertyChanged(nameof(HasValidationError));
            }
        }
    }

    /// <summary>Gets a value indicating whether an inline validation message is shown.</summary>
    public bool HasValidationError => !string.IsNullOrEmpty(_validationError);

    /// <summary>Gets the command that starts a new entry draft.</summary>
    public ICommand NewCommand { get; }

    /// <summary>Gets the command that adds or updates the entry from the draft.</summary>
    public ICommand SaveEntryCommand { get; }

    /// <summary>Gets the command that removes the selected entry.</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>Gets the command that persists the list and re-applies it live.</summary>
    public ICommand ApplyCommand { get; }

    /// <summary>Gets the command that reloads the working copy from the running configuration.</summary>
    public ICommand ReloadCommand { get; }

    /// <summary>Adds or updates an entry from the current draft (M3-RF-612/M3-RF-613).</summary>
    /// <returns>The model result; on rejection the draft stays and the reason is shown inline.</returns>
    public HotkeyEditResult SaveEntry()
    {
        var isNew = _selectedEntry is null;
        var action = ActionEditor.Snapshot();
        var modifiers = DraftModifiers();

        var result = _selectedEntry is { } entry
            ? _model.Edit(entry.Id, _selectedKey, modifiers, action)
            : _model.Add(_selectedKey, modifiers, action);

        if (!result.Succeeded)
        {
            ValidationError = result.Error;
            SetStatus(result.Error ?? "The hotkey could not be saved.", isError: true);
            return result;
        }

        ValidationError = null;
        SetStatus(isNew ? "Hotkey added." : "Hotkey updated.", isError: false);
        RebuildEntries(result.Entry!.Id);
        RaiseDirtyChanged();
        return result;
    }

    /// <summary>Removes the currently selected entry (M3-RF-612).</summary>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public bool RemoveSelected()
    {
        if (_selectedEntry is not { } entry)
        {
            SetStatus("Select a hotkey to remove.", isError: true);
            return false;
        }

        if (!_model.Remove(entry.Id))
        {
            return false;
        }

        NewDraft();
        RebuildEntries(selectId: null);
        RaiseDirtyChanged();
        SetStatus("Hotkey removed.", isError: false);
        return true;
    }

    /// <summary>Starts a new entry draft with the default key/modifier/action (M3-RF-612).</summary>
    public void NewDraft()
    {
        _suppressSelectionReload = true;
        SelectedEntry = null;
        _suppressSelectionReload = false;
        ResetDraft();
        SetStatus("New hotkey. Choose a modifier, a key, and an action, then save.", isError: false);
    }

    /// <summary>Reloads the working copy from the running configuration (used on open / external reload).</summary>
    public void ReloadFromModel()
    {
        if (_reloadSource is not { } source)
        {
            return;
        }

        _model.Reload(source());
        ValidationError = null;
        NewDraft();
        RebuildEntries(selectId: null);
        RaiseDirtyChanged();
        SetStatus("Reloaded the configured hotkeys.", isError: false);
    }

    /// <summary>
    /// Validates the working copy, persists it through the single-writer path, and applies it live
    /// (M3-RF-614/M3-RF-615). A save failure keeps the dirty edits and changes nothing
    /// (M3-RF-617/F-M3-15).
    /// </summary>
    /// <returns><see langword="true"/> when the hotkeys were saved and applied.</returns>
    public async Task<bool> ApplyAsync()
    {
        if (!_model.IsValid)
        {
            ValidationError = "Resolve the highlighted hotkeys before applying.";
            SetStatus("The hotkeys were not applied because some entries are invalid.", isError: true);
            return false;
        }

        if (!_model.IsDirty)
        {
            SetStatus("No hotkey changes to apply.", isError: false);
            return true;
        }

        IsBusy = true;
        try
        {
            var list = _model.ToConfigList();
            var result = await _settings
                .ApplyAsync(new SettingsChange { Hotkeys = list })
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                // The apply hook is never reached on a save failure, so the current registrations
                // stay intact (M3-RF-617/F-M3-15).
                SetStatus(result.ErrorMessage ?? "The hotkeys could not be saved.", isError: true);
                return false;
            }

            _model.MarkSaved();
            RebuildEntries(_selectedEntry?.Id);
            RaiseDirtyChanged();

            if (_hotkeysAvailable)
            {
                SetStatus("Hotkeys saved and applied.", isError: false);
            }
            else
            {
                // The service-unavailable path is surfaced once, per F-M3-15 (log + one notification).
                SetStatus("Hotkeys saved. They are unavailable in this session and take effect on the next start.", isError: false);
                _notifier.Notify(
                    "TouchPanel",
                    "Global hotkeys are unavailable in this session; the changes were saved and take effect on the next start.",
                    NotificationSeverity.Warning);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hotkeys editor: applying the hotkey changes failed unexpectedly");
            SetStatus("The hotkeys could not be applied.", isError: true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadDraftFrom(HotkeyEditorEntry entry)
    {
        _modifierCtrl = HasModifier(entry.Modifiers, "Ctrl");
        _modifierAlt = HasModifier(entry.Modifiers, "Alt");
        _modifierShift = HasModifier(entry.Modifiers, "Shift");
        _modifierWin = HasModifier(entry.Modifiers, "Win");
        _selectedKey = string.IsNullOrWhiteSpace(entry.Key) ? "A" : entry.Key;

        OnPropertyChanged(nameof(ModifierCtrl));
        OnPropertyChanged(nameof(ModifierAlt));
        OnPropertyChanged(nameof(ModifierShift));
        OnPropertyChanged(nameof(ModifierWin));
        OnPropertyChanged(nameof(SelectedKey));

        ActionEditor.Load(entry.Action);
        ValidationError = null;
    }

    private void ResetDraft()
    {
        _modifierCtrl = false;
        _modifierAlt = false;
        _modifierShift = false;
        _modifierWin = false;
        _selectedKey = "A";

        OnPropertyChanged(nameof(ModifierCtrl));
        OnPropertyChanged(nameof(ModifierAlt));
        OnPropertyChanged(nameof(ModifierShift));
        OnPropertyChanged(nameof(ModifierWin));
        OnPropertyChanged(nameof(SelectedKey));

        ActionEditor.Load(new MuteActionConfig());
        ValidationError = null;
    }

    private void RebuildEntries(string? selectId)
    {
        _suppressSelectionReload = true;
        Entries.Clear();
        foreach (var entry in _model.Entries)
        {
            Entries.Add(entry);
        }

        _selectedEntry = selectId is null
            ? null
            : _model.Find(selectId);

        if (_selectedEntry is { } selected)
        {
            LoadDraftFrom(selected);
        }

        _suppressSelectionReload = false;

        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanApply));
    }

    private List<string> DraftModifiers()
    {
        var modifiers = new List<string>(4);
        if (_modifierCtrl)
        {
            modifiers.Add("Ctrl");
        }

        if (_modifierAlt)
        {
            modifiers.Add("Alt");
        }

        if (_modifierShift)
        {
            modifiers.Add("Shift");
        }

        if (_modifierWin)
        {
            modifiers.Add("Win");
        }

        return modifiers;
    }

    private void RaiseDirtyChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanApply));
    }

    private static bool HasModifier(IReadOnlyList<string> modifiers, string name) =>
        modifiers.Any(modifier => string.Equals(modifier, name, StringComparison.OrdinalIgnoreCase));

    private void SetStatus(string? message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    /// <summary>Minimal synchronous <see cref="ICommand"/> for the editor's immediate operations.</summary>
    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            try
            {
                _execute();
            }
            catch (Exception)
            {
                // The view model already converts failures into status/validation state; this guard
                // only keeps a command invocation from surfacing an exception to the dispatcher.
            }
        }
    }
}
