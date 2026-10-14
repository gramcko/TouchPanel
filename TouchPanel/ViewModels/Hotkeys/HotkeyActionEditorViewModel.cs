using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.ViewModels.Hotkeys;

/// <summary>
/// Touch-operable action editor for the focused hotkey editor (M3-RF-612). It offers the same
/// action catalog as the widget property editor — the M1 actions plus <c>sendKeys</c> — with the
/// same typed-parameter editing and the same default-action shapes, but commits into a standalone
/// working copy so the hotkey entry is only changed when the user saves the entry. It is WPF-free
/// apart from change notifications and is unit-testable without a desktop.
/// </summary>
/// <remarks>
/// <para><b>Reuse, not duplication of semantics.</b> The catalog, the friendly key/modifier names,
/// and the volume clamp all come from the shared editor/runtime seams
/// (<see cref="ActionPickerViewModel"/>, <see cref="SendKeyCombination"/>,
/// <see cref="ConfigValidator"/>), so the hotkey editor and the widget editor cannot drift.</para>
/// <para><b>Working copy.</b> <see cref="Load"/> clones the supplied action; <see cref="Snapshot"/>
/// clones it back, so neither the configuration nor the entry is mutated before the user saves.</para>
/// </remarks>
public sealed class HotkeyActionEditorViewModel : INotifyPropertyChanged
{
    /// <summary>The <see cref="ActionTypeOption.Type"/> value that represents no bound action.</summary>
    public const string NoneType = ActionPickerViewModel.NoneType;

    private static readonly ActionTypeOption NoneOption = new(NoneType, "None");

    private static readonly ActionTypeOption[] Catalog =
    [
        NoneOption,
        new(ActionTypes.RunCommand, "Run command"),
        new(ActionTypes.SendKeys, "Send keys"),
        new(ActionTypes.MediaKey, "Media key"),
        new(ActionTypes.Volume, "Volume"),
        new(ActionTypes.Mute, "Mute"),
    ];

    private static readonly MediaKeyKind[] MediaKeysCatalog =
        [MediaKeyKind.PlayPause, MediaKeyKind.Next, MediaKeyKind.Previous, MediaKeyKind.Stop];

    private static readonly VolumeMode[] VolumeModesCatalog = [VolumeMode.Set, VolumeMode.Step];

    private static readonly VolumeDirection[] VolumeDirectionsCatalog =
        [VolumeDirection.Up, VolumeDirection.Down];

    private ActionConfig? _action;
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
    private string? _validationError;

    /// <summary>Initializes a new instance of the <see cref="HotkeyActionEditorViewModel"/> class.</summary>
    public HotkeyActionEditorViewModel()
    {
        ActionTypeOptions = new System.Collections.ObjectModel.ReadOnlyCollection<ActionTypeOption>(Catalog);
        MediaKeyOptions = new System.Collections.ObjectModel.ReadOnlyCollection<MediaKeyKind>(MediaKeysCatalog);
        VolumeModeOptions = new System.Collections.ObjectModel.ReadOnlyCollection<VolumeMode>(VolumeModesCatalog);
        VolumeDirectionOptions = new System.Collections.ObjectModel.ReadOnlyCollection<VolumeDirection>(VolumeDirectionsCatalog);
        ReloadFields();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the selectable action types (None plus the M2 action set).</summary>
    public IReadOnlyList<ActionTypeOption> ActionTypeOptions { get; }

    /// <summary>Gets the selectable media keys.</summary>
    public IReadOnlyList<MediaKeyKind> MediaKeyOptions { get; }

    /// <summary>Gets the selectable volume modes.</summary>
    public IReadOnlyList<VolumeMode> VolumeModeOptions { get; }

    /// <summary>Gets the selectable volume step directions.</summary>
    public IReadOnlyList<VolumeDirection> VolumeDirectionOptions { get; }

    /// <summary>Gets the current validation message, or <see langword="null"/> when the action looks valid.</summary>
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

    /// <summary>Gets a value indicating whether a validation message is shown.</summary>
    public bool HasValidationError => _validationError is not null;

    /// <summary>Gets a value indicating whether the run-command parameter editors apply.</summary>
    public bool CanEditRunCommand => _action is RunCommandActionConfig;

    /// <summary>Gets a value indicating whether the send-keys parameter editors apply.</summary>
    public bool CanEditSendKeys => _action is SendKeysActionConfig;

    /// <summary>Gets a value indicating whether the media-key parameter editor applies.</summary>
    public bool CanEditMediaKey => _action is MediaKeyActionConfig;

    /// <summary>Gets a value indicating whether the volume parameter editors apply.</summary>
    public bool CanEditVolume => _action is VolumeActionConfig;

    /// <summary>Gets a value indicating whether the volume action is in absolute-set mode.</summary>
    public bool IsVolumeSetMode => _action is VolumeActionConfig { Mode: VolumeMode.Set };

    /// <summary>Gets a value indicating whether the volume action is in step mode.</summary>
    public bool IsVolumeStepMode => _action is VolumeActionConfig { Mode: VolumeMode.Step };

    /// <summary>
    /// Gets or sets the chosen action type. Changing it writes a fresh default of that type;
    /// <see cref="NoneType"/> clears the binding (which makes the entry invalid, M3-RF-613).
    /// </summary>
    public ActionTypeOption SelectedActionOption
    {
        get => ActionTypeOptions.FirstOrDefault(option => string.Equals(option.Type, _action?.Type, StringComparison.Ordinal))
            ?? NoneOption;
        set
        {
            var option = value ?? NoneOption;
            if (string.Equals(option.Type, _action?.Type ?? NoneType, StringComparison.Ordinal))
            {
                return;
            }

            _action = CreateDefaultAction(option.Type);
            OnPropertyChanged();
            RaiseActionShapeChanged();
            ReloadFields();
            Validate();
        }
    }

    /// <summary>Gets or sets the run-command executable path.</summary>
    public string PathText
    {
        get => _pathText;
        set
        {
            if (!SetField(ref _pathText, value ?? string.Empty) || _action is not RunCommandActionConfig run)
            {
                return;
            }

            run.Path = _pathText.Trim();
            Validate();
        }
    }

    /// <summary>Gets or sets the run-command arguments as discrete values, one per line.</summary>
    public string ArgumentsText
    {
        get => _argumentsText;
        set
        {
            if (!SetField(ref _argumentsText, value ?? string.Empty) || _action is not RunCommandActionConfig run)
            {
                return;
            }

            run.Arguments = ParseArguments(_argumentsText);
        }
    }

    /// <summary>Gets or sets the run-command working directory.</summary>
    public string WorkingDirectoryText
    {
        get => _workingDirectoryText;
        set
        {
            if (!SetField(ref _workingDirectoryText, value ?? string.Empty) || _action is not RunCommandActionConfig run)
            {
                return;
            }

            var trimmed = _workingDirectoryText.Trim();
            run.WorkingDirectory = trimmed.Length == 0 ? null : trimmed;
        }
    }

    /// <summary>Gets or sets the send-keys friendly key name.</summary>
    public string KeyText
    {
        get => _keyText;
        set
        {
            if (!SetField(ref _keyText, value ?? string.Empty) || _action is not SendKeysActionConfig send)
            {
                return;
            }

            send.Key = _keyText.Trim();
            Validate();
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
            if (SetField(ref _selectedMediaKey, value) && _action is MediaKeyActionConfig media)
            {
                media.Key = _selectedMediaKey;
            }
        }
    }

    /// <summary>Gets or sets the volume mode.</summary>
    public VolumeMode SelectedVolumeMode
    {
        get => _selectedVolumeMode;
        set
        {
            if (!SetField(ref _selectedVolumeMode, value) || _action is not VolumeActionConfig volume)
            {
                return;
            }

            volume.Mode = _selectedVolumeMode;
            OnPropertyChanged(nameof(IsVolumeSetMode));
            OnPropertyChanged(nameof(IsVolumeStepMode));
            Validate();
        }
    }

    /// <summary>Gets or sets the volume step direction.</summary>
    public VolumeDirection SelectedVolumeDirection
    {
        get => _selectedVolumeDirection;
        set
        {
            if (SetField(ref _selectedVolumeDirection, value) && _action is VolumeActionConfig volume)
            {
                volume.Direction = _selectedVolumeDirection;
            }
        }
    }

    /// <summary>Gets or sets the absolute volume level (clamped to 0–100).</summary>
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
                ValidationError = "Level must be a whole number between 0 and 100.";
                return;
            }

            if (_action is not VolumeActionConfig volume)
            {
                return;
            }

            var clamped = Math.Clamp(level, ConfigValidator.MinVolumeLevel, ConfigValidator.MaxVolumeLevel);
            volume.Level = clamped;
            SetField(ref _levelText, clamped.ToString(CultureInfo.InvariantCulture));
            ValidationError = null;
        }
    }

    /// <summary>Loads a working copy of <paramref name="action"/> into the editor.</summary>
    /// <param name="action">The action to edit, or <see langword="null"/> for none.</param>
    public void Load(ActionConfig? action)
    {
        _action = Clone(action);
        OnPropertyChanged(nameof(SelectedActionOption));
        RaiseActionShapeChanged();
        ReloadFields();
        Validate();
    }

    /// <summary>Clones the current working copy so the caller can persist it without aliasing.</summary>
    /// <returns>A fresh action instance, or <see langword="null"/> when no action is bound.</returns>
    public ActionConfig? Snapshot() => Clone(_action);

    /// <summary>Creates a fresh default action of the given type (or <see langword="null"/> for none).</summary>
    /// <param name="type">The action type discriminator.</param>
    /// <returns>The default action, or <see langword="null"/>.</returns>
    public static ActionConfig? CreateDefaultAction(string type) => type switch
    {
        ActionTypes.RunCommand => new RunCommandActionConfig(),
        ActionTypes.SendKeys => new SendKeysActionConfig(),
        ActionTypes.MediaKey => new MediaKeyActionConfig(),
        ActionTypes.Volume => new VolumeActionConfig { Mode = VolumeMode.Set, Level = VolumeActionConfig.DefaultLevel },
        ActionTypes.Mute => new MuteActionConfig(),
        _ => null,
    };

    /// <summary>Creates a deep copy of an action configuration so edits never alias the source.</summary>
    /// <param name="action">The action to clone, or <see langword="null"/>.</param>
    /// <returns>The clone, or <see langword="null"/>.</returns>
    public static ActionConfig? Clone(ActionConfig? action) => action switch
    {
        null => null,
        RunCommandActionConfig run => new RunCommandActionConfig
        {
            Path = run.Path,
            Arguments = [.. run.Arguments],
            WorkingDirectory = run.WorkingDirectory,
        },
        SendKeysActionConfig send => new SendKeysActionConfig
        {
            Key = send.Key,
            Modifiers = [.. send.Modifiers],
        },
        MediaKeyActionConfig media => new MediaKeyActionConfig { Key = media.Key },
        VolumeActionConfig volume => new VolumeActionConfig
        {
            Mode = volume.Mode,
            Level = volume.Level,
            Direction = volume.Direction,
        },
        MuteActionConfig => new MuteActionConfig(),
        UnknownActionConfig unknown => new UnknownActionConfig(unknown.Type, unknown.Raw),
        _ => null,
    };

    private void SetModifier(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return;
        }

        if (_action is not SendKeysActionConfig send)
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
        Validate();
    }

    private void Validate()
    {
        ValidationError = _action switch
        {
            null => null,
            RunCommandActionConfig run when string.IsNullOrWhiteSpace(run.Path) => "Enter the executable path.",
            SendKeysActionConfig send when !SendKeyCombination.TryMapKey(send.Key, out _) => "Choose a key for the send-keys action.",
            _ => null,
        };
    }

    private void ReloadFields()
    {
        _pathText = _action is RunCommandActionConfig run ? run.Path : string.Empty;
        _argumentsText = _action is RunCommandActionConfig runArguments
            ? string.Join(Environment.NewLine, runArguments.Arguments)
            : string.Empty;
        _workingDirectoryText = _action is RunCommandActionConfig runWorking ? runWorking.WorkingDirectory ?? string.Empty : string.Empty;

        var send = _action as SendKeysActionConfig;
        _keyText = send?.Key ?? string.Empty;
        _modifierCtrl = HasModifier(send, "Ctrl");
        _modifierAlt = HasModifier(send, "Alt");
        _modifierShift = HasModifier(send, "Shift");
        _modifierWin = HasModifier(send, "Win");

        _selectedMediaKey = _action is MediaKeyActionConfig media ? media.Key : MediaKeyKind.PlayPause;
        _selectedVolumeMode = _action is VolumeActionConfig volume ? volume.Mode : VolumeMode.Set;
        _selectedVolumeDirection = _action is VolumeActionConfig volumeDirection ? volumeDirection.Direction : VolumeDirection.Up;
        _levelText = (_action is VolumeActionConfig volumeLevel ? volumeLevel.Level : VolumeActionConfig.DefaultLevel)
            .ToString(CultureInfo.InvariantCulture);

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
        OnPropertyChanged(nameof(IsVolumeSetMode));
        OnPropertyChanged(nameof(IsVolumeStepMode));
        OnPropertyChanged(nameof(SelectedActionOption));
    }

    private void RaiseActionShapeChanged()
    {
        OnPropertyChanged(nameof(CanEditRunCommand));
        OnPropertyChanged(nameof(CanEditSendKeys));
        OnPropertyChanged(nameof(CanEditMediaKey));
        OnPropertyChanged(nameof(CanEditVolume));
        OnPropertyChanged(nameof(IsVolumeSetMode));
        OnPropertyChanged(nameof(IsVolumeStepMode));
    }

    private static bool HasModifier(SendKeysActionConfig? send, string name) =>
        send is not null
        && send.Modifiers.Exists(modifier => string.Equals(modifier, name, StringComparison.OrdinalIgnoreCase));

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
