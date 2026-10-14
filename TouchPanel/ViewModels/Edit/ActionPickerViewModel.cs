using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Input;

namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// One selectable entry in the action-type catalog (M2-RF-434): the config discriminator and the
/// touch-friendly label. <see cref="NoneType"/> represents an unbound action.
/// </summary>
/// <param name="Type">The config <c>type</c> discriminator, or <see cref="ActionPickerViewModel.NoneType"/> for no action.</param>
/// <param name="DisplayName">The label shown in the action picker.</param>
public sealed record ActionTypeOption(string Type, string DisplayName);

/// <summary>
/// One selectable configured-hotkey entry in the action picker (M5-RF-522): the referenced hotkey
/// id and a human-readable label (combination + id when the combination is parseable, otherwise the
/// id alone).
/// </summary>
/// <param name="Id">The referenced <see cref="HotkeyConfig.Id"/> written to the action.</param>
/// <param name="DisplayName">The label shown in the hotkey picker.</param>
public sealed record HotkeyOption(string Id, string DisplayName);

/// <summary>
/// Touch-operable action picker for the edit-mode property editor (T-14, RF-043/M2-RF-434). It
/// projects the selected widget's bound action into the M2 action set — the M1 types (run command /
/// "open program", media key, volume, mute) plus <c>sendKeys</c> — offers per-type parameter editors,
/// and provides an executable <b>file browse</b> for the run-command action so an <c>.exe</c> can be
/// chosen without hand-editing the path. Choosing an action writes a fresh default of that type,
/// validated with the same rules <see cref="ConfigValidator"/> uses; an invalid or missing action
/// leaves the widget disabled with a visible reason and a log entry (RF-047) but never drops it.
/// </summary>
/// <remarks>
/// <para><b>Live binding (M2-RF-434).</b> Every accepted edit mutates the in-memory
/// <see cref="WidgetConfig"/> and commits through <see cref="PropertyEditorViewModel"/>, which
/// projects the change onto the existing widget view model (no rebuild) and marks the layout dirty.
/// Persistence stays on the existing single-writer save-on-exit path (M2-RF-437).</para>
/// <para><b>Arguments (M2-RF-434/RF-049).</b> Run-command arguments are edited as discrete values,
/// one per line, so a value that contains spaces is never split and no shell command string is ever
/// built (the M1 safe-launch path is unchanged).</para>
/// <para><b>Browse seam.</b> <see cref="BrowseExecutableCommand"/> delegates to the injected
/// <see cref="IExecutablePicker"/>; without one (for example a display-only caller) the command is a
/// no-op and the path stays editable by hand.</para>
/// <para><b>Threading.</b> All editing runs on the UI thread (edit mode); no I/O happens here.</para>
/// </remarks>
public sealed class ActionPickerViewModel : INotifyPropertyChanged
{
    /// <summary>The <see cref="ActionTypeOption.Type"/> value that represents no bound action.</summary>
    public const string NoneType = "";

    private static readonly ActionTypeOption NoneOption = new(NoneType, "None");

    private static readonly ActionTypeOption[] Catalog =
    [
        NoneOption,
        new(ActionTypes.RunCommand, "Run command"),
        new(ActionTypes.SendKeys, "Send keys"),
        new(ActionTypes.MediaKey, "Media key"),
        new(ActionTypes.Volume, "Volume"),
        new(ActionTypes.Mute, "Mute"),
        new(ActionTypes.Hotkey, "Hotkey"),
    ];

    private static readonly MediaKeyKind[] MediaKeysCatalog =
        [MediaKeyKind.PlayPause, MediaKeyKind.Next, MediaKeyKind.Previous, MediaKeyKind.Stop];

    private static readonly VolumeMode[] VolumeModesCatalog =
        [VolumeMode.Set, VolumeMode.Step];

    private static readonly VolumeDirection[] VolumeDirectionsCatalog =
        [VolumeDirection.Up, VolumeDirection.Down];

    private readonly PropertyEditorViewModel _editor;
    private readonly WidgetConfig _config;
    private readonly IExecutablePicker? _executablePicker;
    private readonly IReadOnlyList<HotkeyConfig> _hotkeys;
    private readonly ILogger _logger;

    private string _pathText = string.Empty;
    private string _argumentsText = string.Empty;
    private string _workingDirectoryText = string.Empty;
    private string _keyText = string.Empty;
    private bool _modifierCtrl;
    private bool _modifierAlt;
    private bool _modifierShift;
    private bool _modifierWin;
    private MediaKeyKind _selectedMediaKey = MediaKeyKind.PlayPause;
    private VolumeMode _selectedVolumeMode = VolumeMode.Set;
    private VolumeDirection _selectedVolumeDirection = VolumeDirection.Up;
    private string _levelText = VolumeActionConfig.DefaultLevel.ToString(CultureInfo.InvariantCulture);
    private HotkeyOption? _selectedHotkey;
    private string? _validationError;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionPickerViewModel"/> class for one widget's
    /// bound action, seeding the per-type fields from the current configuration.
    /// </summary>
    /// <param name="editor">The owning property editor that commits accepted edits; must not be <see langword="null"/>.</param>
    /// <param name="config">The widget configuration whose action is edited; must not be <see langword="null"/>.</param>
    /// <param name="executablePicker">The executable browse seam; <see langword="null"/> disables browsing.</param>
    /// <param name="logger">Logger for invalid action selections (RF-047); defaults to the global Serilog logger.</param>
    /// <param name="hotkeys">
    /// The configured global hotkeys offered by the <c>hotkey</c> action (M5-RF-522); when
    /// <see langword="null"/>, the hotkey list is empty and the picker shows the "no hotkeys" state.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public ActionPickerViewModel(
        PropertyEditorViewModel editor,
        WidgetConfig config,
        IExecutablePicker? executablePicker = null,
        ILogger? logger = null,
        IReadOnlyList<HotkeyConfig>? hotkeys = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(config);

        _editor = editor;
        _config = config;
        _executablePicker = executablePicker;
        _logger = (logger ?? Log.Logger).ForContext<ActionPickerViewModel>();
        _hotkeys = hotkeys ?? [];

        ActionTypeOptions = new ReadOnlyCollection<ActionTypeOption>(Catalog);
        MediaKeyOptions = new ReadOnlyCollection<MediaKeyKind>(MediaKeysCatalog);
        VolumeModeOptions = new ReadOnlyCollection<VolumeMode>(VolumeModesCatalog);
        VolumeDirectionOptions = new ReadOnlyCollection<VolumeDirection>(VolumeDirectionsCatalog);
        HotkeyOptions = BuildHotkeyOptions(_hotkeys);

        BrowseExecutableCommand = new AsyncRelayCommand(() =>
        {
            BrowseExecutable();
            return Task.CompletedTask;
        });

        ReloadFieldsFromAction();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the selectable action types (None plus the M2 action set); unknown types never appear.</summary>
    public IReadOnlyList<ActionTypeOption> ActionTypeOptions { get; }

    /// <summary>Gets the selectable media keys (the unknown sentinel is never offered).</summary>
    public IReadOnlyList<MediaKeyKind> MediaKeyOptions { get; }

    /// <summary>Gets the selectable volume modes (the unknown sentinel is never offered).</summary>
    public IReadOnlyList<VolumeMode> VolumeModeOptions { get; }

    /// <summary>Gets the selectable volume step directions (the unknown sentinel is never offered).</summary>
    public IReadOnlyList<VolumeDirection> VolumeDirectionOptions { get; }

    /// <summary>
    /// Gets the configured hotkeys offered by the <c>hotkey</c> action (M5-RF-522), in configuration
    /// order, each labeled with its combination when parseable. Empty when no hotkeys are configured.
    /// </summary>
    public IReadOnlyList<HotkeyOption> HotkeyOptions { get; }

    /// <summary>Gets a value indicating whether any hotkey is available to reference (M5-RF-522).</summary>
    public bool HasHotkeys => HotkeyOptions.Count > 0;

    /// <summary>Gets a value indicating whether the "no hotkeys configured yet" state should be shown (M5-RF-522).</summary>
    public bool HasNoHotkeys => HotkeyOptions.Count == 0;

    /// <summary>Gets the command that opens the executable browse (M2-RF-434); no-op without a picker seam.</summary>
    public ICommand BrowseExecutableCommand { get; }

    /// <summary>Gets the current validation message, or <see langword="null"/> when the last action was accepted.</summary>
    public string? ValidationError => _validationError;

    /// <summary>Gets a value indicating whether a validation message is shown.</summary>
    public bool HasValidationError => _validationError is not null;

    /// <summary>Gets a value indicating whether the run-command parameter editors apply.</summary>
    public bool CanEditRunCommand => _config.Action is RunCommandActionConfig;

    /// <summary>Gets a value indicating whether the send-keys parameter editors apply.</summary>
    public bool CanEditSendKeys => _config.Action is SendKeysActionConfig;

    /// <summary>Gets a value indicating whether the media-key parameter editor applies.</summary>
    public bool CanEditMediaKey => _config.Action is MediaKeyActionConfig;

    /// <summary>Gets a value indicating whether the volume parameter editors apply.</summary>
    public bool CanEditVolume => _config.Action is VolumeActionConfig;

    /// <summary>Gets a value indicating whether the hotkey selector applies (M5-RF-522).</summary>
    public bool CanEditHotkey => _config.Action is RunHotkeyActionConfig;

    /// <summary>Gets a value indicating whether the volume action is in absolute-set mode.</summary>
    public bool IsVolumeSetMode => _config.Action is VolumeActionConfig { Mode: VolumeMode.Set };

    /// <summary>Gets a value indicating whether the volume action is in step mode.</summary>
    public bool IsVolumeStepMode => _config.Action is VolumeActionConfig { Mode: VolumeMode.Step };

    /// <summary>
    /// Gets or sets the chosen action type (M2-RF-434). Changing it writes a fresh default action of
    /// that type and commits it; <see cref="NoneType"/> clears the binding (which disables a
    /// Button/Toggle, per the config validator).
    /// </summary>
    public ActionTypeOption SelectedActionOption
    {
        get => ActionTypeOptions.FirstOrDefault(option => string.Equals(option.Type, CurrentType, StringComparison.Ordinal))
            ?? NoneOption;
        set
        {
            var option = value ?? NoneOption;
            if (string.Equals(option.Type, CurrentType, StringComparison.Ordinal))
            {
                return;
            }

            _config.Action = CreateDefaultAction(option.Type);
            OnPropertyChanged();
            RaiseActionShapeChanged();
            ReloadFieldsFromAction();
            Commit();
        }
    }

    /// <summary>Gets or sets the run-command executable path; a blank value disables the widget.</summary>
    public string PathText
    {
        get => _pathText;
        set
        {
            if (!SetField(ref _pathText, value ?? string.Empty) || _config.Action is not RunCommandActionConfig run)
            {
                return;
            }

            run.Path = _pathText.Trim();
            Commit();
        }
    }

    /// <summary>
    /// Gets or sets the run-command arguments as discrete values, one per line (M2-RF-434). Blank
    /// lines are dropped; a value containing spaces is preserved as a single argument.
    /// </summary>
    public string ArgumentsText
    {
        get => _argumentsText;
        set
        {
            if (!SetField(ref _argumentsText, value ?? string.Empty) || _config.Action is not RunCommandActionConfig run)
            {
                return;
            }

            run.Arguments = ParseArguments(_argumentsText);
            Commit();
        }
    }

    /// <summary>Gets or sets the run-command working directory; a blank value is stored as none.</summary>
    public string WorkingDirectoryText
    {
        get => _workingDirectoryText;
        set
        {
            if (!SetField(ref _workingDirectoryText, value ?? string.Empty) || _config.Action is not RunCommandActionConfig run)
            {
                return;
            }

            var trimmed = _workingDirectoryText.Trim();
            run.WorkingDirectory = trimmed.Length == 0 ? null : trimmed;
            Commit();
        }
    }

    /// <summary>Gets or sets the send-keys friendly key name; an unknown/blank name disables the widget.</summary>
    public string KeyText
    {
        get => _keyText;
        set
        {
            if (!SetField(ref _keyText, value ?? string.Empty) || _config.Action is not SendKeysActionConfig send)
            {
                return;
            }

            send.Key = _keyText.Trim();
            Commit();
        }
    }

    /// <summary>Gets or sets whether the <c>Ctrl</c> modifier is part of the send-keys combination.</summary>
    public bool ModifierCtrl
    {
        get => _modifierCtrl;
        set => SetModifier(ref _modifierCtrl, value);
    }

    /// <summary>Gets or sets whether the <c>Alt</c> modifier is part of the send-keys combination.</summary>
    public bool ModifierAlt
    {
        get => _modifierAlt;
        set => SetModifier(ref _modifierAlt, value);
    }

    /// <summary>Gets or sets whether the <c>Shift</c> modifier is part of the send-keys combination.</summary>
    public bool ModifierShift
    {
        get => _modifierShift;
        set => SetModifier(ref _modifierShift, value);
    }

    /// <summary>Gets or sets whether the <c>Win</c> modifier is part of the send-keys combination.</summary>
    public bool ModifierWin
    {
        get => _modifierWin;
        set => SetModifier(ref _modifierWin, value);
    }

    /// <summary>Gets or sets the media key.</summary>
    public MediaKeyKind SelectedMediaKey
    {
        get => _selectedMediaKey;
        set
        {
            if (!SetField(ref _selectedMediaKey, value) || _config.Action is not MediaKeyActionConfig media)
            {
                return;
            }

            media.Key = _selectedMediaKey;
            Commit();
        }
    }

    /// <summary>Gets or sets the volume mode (set absolute level or step up/down).</summary>
    public VolumeMode SelectedVolumeMode
    {
        get => _selectedVolumeMode;
        set
        {
            if (!SetField(ref _selectedVolumeMode, value) || _config.Action is not VolumeActionConfig volume)
            {
                return;
            }

            volume.Mode = _selectedVolumeMode;
            OnPropertyChanged(nameof(IsVolumeSetMode));
            OnPropertyChanged(nameof(IsVolumeStepMode));
            Commit();
        }
    }

    /// <summary>Gets or sets the volume step direction.</summary>
    public VolumeDirection SelectedVolumeDirection
    {
        get => _selectedVolumeDirection;
        set
        {
            if (!SetField(ref _selectedVolumeDirection, value) || _config.Action is not VolumeActionConfig volume)
            {
                return;
            }

            volume.Direction = _selectedVolumeDirection;
            Commit();
        }
    }

    /// <summary>Gets or sets the absolute volume level for set mode; clamped to <c>0–100</c>.</summary>
    public string LevelText
    {
        get => _levelText;
        set
        {
            if (!SetField(ref _levelText, value ?? string.Empty))
            {
                return;
            }

            if (!int.TryParse(_levelText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
            {
                SetError("Level must be a whole number between 0 and 100.");
                return;
            }

            if (_config.Action is not VolumeActionConfig volume)
            {
                return;
            }

            var clamped = Math.Clamp(level, ConfigValidator.MinVolumeLevel, ConfigValidator.MaxVolumeLevel);
            volume.Level = clamped;
            SetField(ref _levelText, clamped.ToString(CultureInfo.InvariantCulture));
            Commit();
        }
    }

    private string CurrentType => _config.Action?.Type ?? NoneType;

    /// <summary>
    /// Gets or sets the referenced configured hotkey for a <c>hotkey</c> action (M5-RF-522).
    /// Selecting an entry writes its id to the action and commits with the same validation cross-check
    /// the load-time validator uses; a <see langword="null"/> assignment is ignored.
    /// </summary>
    public HotkeyOption? SelectedHotkey
    {
        get
        {
            if (_config.Action is not RunHotkeyActionConfig hotkey || string.IsNullOrWhiteSpace(hotkey.HotkeyId))
            {
                return null;
            }

            return HotkeyOptions.FirstOrDefault(option =>
                string.Equals(option.Id, hotkey.HotkeyId, StringComparison.Ordinal));
        }
        set
        {
            if (value is not { } option || _config.Action is not RunHotkeyActionConfig hotkey)
            {
                return;
            }

            if (!SetField(ref _selectedHotkey, option) || string.Equals(hotkey.HotkeyId, option.Id, StringComparison.Ordinal))
            {
                return;
            }

            hotkey.HotkeyId = option.Id;
            Commit();
        }
    }

    private static ActionConfig? CreateDefaultAction(string type) => type switch
    {
        ActionTypes.RunCommand => new RunCommandActionConfig(),
        ActionTypes.SendKeys => new SendKeysActionConfig(),
        ActionTypes.MediaKey => new MediaKeyActionConfig(),
        ActionTypes.Volume => new VolumeActionConfig { Mode = VolumeMode.Set, Level = VolumeActionConfig.DefaultLevel },
        ActionTypes.Mute => new MuteActionConfig(),
        ActionTypes.Hotkey => new RunHotkeyActionConfig(),
        _ => null,
    };

    /// <summary>
    /// Builds the selectable hotkey options (M5-RF-522): each configured hotkey's id plus a
    /// human-readable combination when the entry parses. Ids are never dropped, so a malformed
    /// hotkey still appears and remains selectable by id.
    /// </summary>
    /// <param name="hotkeys">The configured hotkeys in configuration order.</param>
    /// <returns>The option list; empty when no hotkeys are configured.</returns>
    private static ReadOnlyCollection<HotkeyOption> BuildHotkeyOptions(IReadOnlyList<HotkeyConfig> hotkeys)
    {
        var options = new List<HotkeyOption>(hotkeys.Count);
        foreach (var hotkey in hotkeys)
        {
            if (hotkey is null || string.IsNullOrWhiteSpace(hotkey.Id))
            {
                continue;
            }

            var label = HotKeyParser.TryParse(hotkey, 1, out var binding, out _) && binding is not null
                ? $"{binding.Combination} ({hotkey.Id})"
                : hotkey.Id;
            options.Add(new HotkeyOption(hotkey.Id, label));
        }

        return new ReadOnlyCollection<HotkeyOption>(options);
    }

    private void SetModifier(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return;
        }

        UpdateModifiers();
    }

    private void UpdateModifiers()
    {
        if (_config.Action is not SendKeysActionConfig send)
        {
            return;
        }

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

        send.Modifiers = modifiers;
        Commit();
    }

    /// <summary>
    /// Splits the run-command arguments text into discrete values, one per line. The split is
    /// deliberately line-based so an argument that contains spaces survives intact and no shell
    /// parsing is introduced (RF-049/M2-RF-434).
    /// </summary>
    /// <param name="text">The multi-line arguments text.</param>
    /// <returns>The non-empty argument values in order.</returns>
    private static List<string> ParseArguments(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var arguments = new List<string>();
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                arguments.Add(trimmed);
            }
        }

        return arguments;
    }

    /// <summary>
    /// Commits the current action configuration (M2-RF-434): re-enables the widget, runs the
    /// validator's action rules, surfaces the disabling reason (or clears it), and asks the property
    /// editor to project the change live and mark the layout dirty. Unknown/invalid actions leave
    /// the widget disabled with the validator's reason; the widget is never dropped.
    /// </summary>
    private void Commit()
    {
        _config.IsEnabled = true;
        _config.DisabledReason = null;
        ConfigValidator.ValidateWidgetAction(_config, _hotkeys);

        if (_config.IsEnabled)
        {
            SetError(null);
        }
        else
        {
            // RF-047/M2-RF-434: an invalid action disables the widget with a visible reason; record
            // a redaction-safe log entry so the cause is traceable without capturing user content.
            SetError(_config.DisabledReason);
            _logger.Warning(
                "Action picker: widget {WidgetId} has an invalid action and was disabled",
                _config.Id);
        }

        _editor.ApplyActionEdit();
    }

    private void BrowseExecutable()
    {
        var picker = _executablePicker;
        if (picker is null)
        {
            return;
        }

        var picked = picker.PickExecutable();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            PathText = picked;
        }
    }

    private void ReloadFieldsFromAction()
    {
        var action = _config.Action;

        _pathText = action is RunCommandActionConfig run ? run.Path : string.Empty;
        _argumentsText = action is RunCommandActionConfig runArguments
            ? string.Join(Environment.NewLine, runArguments.Arguments)
            : string.Empty;
        _workingDirectoryText = action is RunCommandActionConfig runWorking ? runWorking.WorkingDirectory ?? string.Empty : string.Empty;

        var send = action as SendKeysActionConfig;
        _keyText = send?.Key ?? string.Empty;
        _modifierCtrl = HasModifier(send, "Ctrl");
        _modifierAlt = HasModifier(send, "Alt");
        _modifierShift = HasModifier(send, "Shift");
        _modifierWin = HasModifier(send, "Win");

        _selectedMediaKey = action is MediaKeyActionConfig media ? media.Key : MediaKeyKind.PlayPause;
        _selectedVolumeMode = action is VolumeActionConfig volume ? volume.Mode : VolumeMode.Set;
        _selectedVolumeDirection = action is VolumeActionConfig volumeDirection ? volumeDirection.Direction : VolumeDirection.Up;
        _levelText = (action is VolumeActionConfig volumeLevel
            ? volumeLevel.Level
            : VolumeActionConfig.DefaultLevel).ToString(CultureInfo.InvariantCulture);

        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(ArgumentsText));
        OnPropertyChanged(nameof(WorkingDirectoryText));
        OnPropertyChanged(nameof(KeyText));
        OnPropertyChanged(nameof(ModifierCtrl));
        OnPropertyChanged(nameof(ModifierAlt));
        OnPropertyChanged(nameof(ModifierShift));
        OnPropertyChanged(nameof(ModifierWin));
        OnPropertyChanged(nameof(SelectedMediaKey));
        OnPropertyChanged(nameof(SelectedVolumeMode));
        OnPropertyChanged(nameof(SelectedVolumeDirection));
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(SelectedHotkey));
        OnPropertyChanged(nameof(IsVolumeSetMode));
        OnPropertyChanged(nameof(IsVolumeStepMode));
    }

    private void RaiseActionShapeChanged()
    {
        OnPropertyChanged(nameof(CanEditRunCommand));
        OnPropertyChanged(nameof(CanEditSendKeys));
        OnPropertyChanged(nameof(CanEditMediaKey));
        OnPropertyChanged(nameof(CanEditVolume));
        OnPropertyChanged(nameof(CanEditHotkey));
        OnPropertyChanged(nameof(SelectedHotkey));
        OnPropertyChanged(nameof(IsVolumeSetMode));
        OnPropertyChanged(nameof(IsVolumeStepMode));
    }

    private static bool HasModifier(SendKeysActionConfig? send, string name) =>
        send is not null
        && send.Modifiers.Exists(modifier => string.Equals(modifier, name, StringComparison.OrdinalIgnoreCase));

    private void SetError(string? message)
    {
        if (string.Equals(_validationError, message, StringComparison.Ordinal))
        {
            return;
        }

        _validationError = message;
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(HasValidationError));
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
}
