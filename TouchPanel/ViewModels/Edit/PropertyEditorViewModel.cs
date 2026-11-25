using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;
using TouchPanel.Services.Time;
using TouchPanel.Theme;

namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// Touch-operable property editor state for the widget selected in edit mode (RF-043/M2-RF-433,
/// T-13). It projects the selected widget's configuration into editable fields, validates every
/// input with the same bounds the <see cref="ConfigValidator"/> uses, blocks or clamps invalid
/// values with visible feedback, and — only on a valid commit — mutates the in-memory
/// <see cref="WidgetConfig"/> and asks the owning <see cref="EditModeService"/> to refresh the live
/// widget and mark the layout dirty. Nothing is written to disk here; persistence stays on the
/// existing single-writer save-on-exit path (M2-RF-437).
/// </summary>
/// <remarks>
/// <para><b>Validation (M2-RF-433).</b> Label is bounded to <see cref="MaxLabelLength"/> characters;
/// gauge/slider ranges and steps mirror <see cref="ConfigValidator"/> (finite, magnitude-bounded,
/// positive span, step within range); the slider initial value is clamped and snapped by the same
/// <see cref="ConfigValidator.NormalizeSliderValue"/> rule the validator uses; the sparkline window is
/// clamped to its configured bounds; the sensor must be non-empty. An invalid input leaves the
/// configuration untouched and raises <see cref="ValidationError"/>; the offending field keeps the
/// user's text so it can be corrected.</para>
/// <para><b>Live update (M2-RF-433).</b> A valid commit calls <see cref="EditModeService.ApplyPropertyEdit"/>,
/// which projects the changed config onto the existing widget view model (no rebuild) and marks the
/// layout dirty. Geometry fields route through the existing clamp helpers so position/size are
/// always finite, in-page, and at least the 40 DIP minimum.</para>
/// <para><b>Threading.</b> All editing runs on the UI thread (edit mode); no I/O happens here.</para>
/// </remarks>
public sealed class PropertyEditorViewModel : INotifyPropertyChanged, INumericFieldHost
{
    /// <summary>Maximum accepted label length in characters (M2-RF-435).</summary>
    public const int MaxLabelLength = 128;

    /// <summary>
    /// The increment/decrement applied by one font-size stepper tap or Up/Down keypress
    /// (user-requested font-size stepper), in DIPs.
    /// </summary>
    public const double FontSizeStepValue = 1;

    private readonly EditModeService _owner;
    private readonly WidgetViewModelBase _widget;
    private readonly WidgetConfig _config;
    private readonly CultureInfo _clockCulture;
    private readonly ISensorCatalog _sensorCatalog;
    private readonly ThemeAppearanceDefault _themeDefaults;
    private readonly IImageFilePicker? _imageFilePicker;

    private string _labelText;
    private string _xText;
    private string _yText;
    private string _widthText;
    private string _heightText;
    private string _sensorText;
    private string _minimumText;
    private string _maximumText;
    private string _stepText;
    private string _initialValueText;
    private string _windowText;
    private IconOption _selectedIcon = NoneIcon;
    private IconOption _selectedLabelIcon = NoneIcon;
    private IconOption _selectedOnIcon = NoneIcon;
    private string _foregroundText = string.Empty;
    private string _backgroundText = string.Empty;
    private string _accentText = string.Empty;
    private string _onIconColorText = string.Empty;
    private string _fontSizeText = string.Empty;
    private string _opacityText = string.Empty;
    private string _formatText = string.Empty;
    private string _imagePathText = string.Empty;
    private bool _showLabel;
    private bool _showValue;
    private bool _rateScaleAdopted;
    private string? _validationError;

    // Live-edit session state (user-requested live preview): the active numeric field, whether the
    // group's single undo point has already been recorded, whether focus opened the group (typing)
    // or a stepper did (one hold/tap is its own group), and the last text that produced a valid,
    // applied value (restored when an invalid commit is attempted).
    private string? _liveEditProperty;
    private bool _liveEditUndoRecorded;
    private bool _liveEditFromFocus;
    private string _liveEditLastValidText = string.Empty;

    /// <summary>The icon-picker entry that represents "no icon" (the widget shows none).</summary>
    private static readonly IconOption NoneIcon = new(string.Empty, string.Empty, "(none)");

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyEditorViewModel"/> class for one selected
    /// widget and its page configuration entry.
    /// </summary>
    /// <param name="owner">The edit-mode service that owns dirty tracking and the live widget; must not be <see langword="null"/>.</param>
    /// <param name="widget">The selected widget view model; must not be <see langword="null"/>.</param>
    /// <param name="config">The selected widget's configuration entry; must not be <see langword="null"/>.</param>
    /// <param name="executablePicker">The executable browse seam for the run-command action picker (M2-RF-434); <see langword="null"/> disables browsing.</param>
    /// <param name="logger">Logger for invalid action selections (RF-047); defaults to the global Serilog logger.</param>
    /// <param name="clockCulture">
    /// The canonical Clock display-culture source (M4-RF-951) used to validate a custom Clock format
    /// with the exact rule <see cref="ConfigValidator.ValidateClock"/> uses; when
    /// <see langword="null"/>, <see cref="ClockCultureSource.Default"/> is used, matching the
    /// validator's fallback.
    /// </param>
    /// <param name="sensorCatalog">
    /// The WPF-free sensor catalog (M5-RF-510) that supplies the selectable sensor keys for the
    /// gauge/sparkline/bar sensor picker; when <see langword="null"/>, a catalog without a snapshot
    /// source is used, which still offers the fixed aggregate keys.
    /// </param>
    /// <param name="hotkeys">
    /// The configured global hotkeys (M5-RF-522) offered by the <c>hotkey</c> action picker; when
    /// <see langword="null"/> the action picker shows the "no hotkeys configured" state.
    /// </param>
    /// <param name="imageFilePicker">
    /// The image-file browse seam for the Image widget's path field (user-requested addition);
    /// <see langword="null"/> disables browsing (the path stays editable by hand).
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public PropertyEditorViewModel(
        EditModeService owner,
        WidgetViewModelBase widget,
        WidgetConfig config,
        IExecutablePicker? executablePicker = null,
        ILogger? logger = null,
        IClockCultureSource? clockCulture = null,
        ISensorCatalog? sensorCatalog = null,
        IReadOnlyList<HotkeyConfig>? hotkeys = null,
        IImageFilePicker? imageFilePicker = null,
        IRecentColorPalette? recentColors = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(config);

        _owner = owner;
        _widget = widget;
        _config = config;
        _clockCulture = (clockCulture ?? ClockCultureSource.Default).Culture;
        _sensorCatalog = sensorCatalog ?? new SensorCatalog();
        _imageFilePicker = imageFilePicker;

        // The picker flyout (user-requested) is transient editor state; it shares the application's
        // MRU palette when one is supplied so widget and page-background selections stay in sync.
        Flyout = new PropertyPickerFlyoutViewModel(this, recentColors ?? new RecentColorPalette());
        ToggleIconFlyoutCommand = new RelayCommand<string>(target =>
        {
            if (TryParseTarget(target, PropertyPickerKind.Icon, out var parsed))
            {
                Flyout.ToggleIcon(parsed);
            }
        });
        ToggleColorFlyoutCommand = new RelayCommand<string>(target =>
        {
            if (TryParseTarget(target, PropertyPickerKind.Color, out var parsed))
            {
                Flyout.ToggleColor(parsed);
            }
        });

        // M5-RF-542: the active theme's appearance defaults. They supply the editor's inherited
        // values for fields the widget leaves null (no blanks, no hard-coded constants); an explicit
        // per-widget override always wins (M5-RF-543).
        _themeDefaults = ThemeAppearanceDefaults.For(_owner.ActiveThemeId);

        // M2-RF-433/M2-RF-434/M4-RF-432: the action-bearing widgets (Button/Toggle/ImageButton) get
        // an action picker; the requested action catalog and the type-specific parameter editors live
        // on it.
        ActionPicker = config is ButtonWidgetConfig or ToggleWidgetConfig or ImageButtonWidgetConfig
            ? new ActionPickerViewModel(this, config, executablePicker, logger, hotkeys)
            : null;

        _labelText = config.Label ?? string.Empty;
        _xText = FormatNumber(widget.X);
        _yText = FormatNumber(widget.Y);
        _widthText = FormatNumber(widget.Width);
        _heightText = FormatNumber(widget.Height);
        _sensorText = ReadSensor() ?? string.Empty;
        SensorOptions = _sensorCatalog.GetSensorOptions(ReadSensor());
        _minimumText = HasRange ? FormatNumber(ReadMinimum()) : string.Empty;
        _maximumText = HasRange ? FormatNumber(ReadMaximum()) : string.Empty;
        _stepText = HasStep ? FormatNumber(ReadStep()) : string.Empty;
        _initialValueText = HasInitialValue ? FormatNumber(ReadValue()) : string.Empty;
        _windowText = HasWindow ? FormatNumber(ReadWindow()) : string.Empty;
        _formatText = config is ClockWidgetConfig clock ? clock.Format ?? string.Empty : string.Empty;
        _imagePathText = config is ImageWidgetConfig image ? image.Path ?? string.Empty : string.Empty;
        _showLabel = config is ImageButtonWidgetConfig imageButton && imageButton.ShowLabel;
        _showValue = config switch
        {
            BarWidgetConfig bar => bar.ShowValue,
            SparklineWidgetConfig sparkline => sparkline.ShowValue,
            _ => false,
        };

        // RF-043/M2-RF-435/M2-RF-436: appearance overrides seed from the widget config; null means
        // "use the theme default" and is shown as an empty field.
        var iconOptions = new List<IconOption>(IconCatalog.BuiltIn.Count + 1) { NoneIcon };
        iconOptions.AddRange(IconCatalog.BuiltIn);
        IconOptions = iconOptions;

        // The label-icon picker offers exactly the same "none + built-in catalog" entries; an
        // unknown configured id shows as "none" (validation clears it on load).
        LabelIconOptions = iconOptions;
        _selectedIcon = IconCatalog.TryGet(config.Icon, out var icon) ? icon : NoneIcon;
        _selectedLabelIcon = IconCatalog.TryGet(config.LabelIcon, out var labelIcon) ? labelIcon : NoneIcon;
        OnIconOptions = iconOptions;
        _selectedOnIcon = config is ToggleWidgetConfig toggleConfig
            && IconCatalog.TryGet(toggleConfig.OnIcon, out var onIcon)
                ? onIcon
                : NoneIcon;
        _foregroundText = config.Foreground ?? _themeDefaults.Foreground;
        _backgroundText = config.Background ?? _themeDefaults.Background;
        _accentText = config.Accent ?? _themeDefaults.Accent;
        _onIconColorText = config is ToggleWidgetConfig { OnIconColor: { } onIconColor } ? onIconColor : string.Empty;
        _fontSizeText = config.FontSize is double fontSize ? FormatNumber(fontSize) : FormatNumber(_themeDefaults.FontSize);
        _opacityText = config.Opacity is double opacity ? FormatNumber(opacity) : FormatNumber(_themeDefaults.Opacity);

        // M5-RF-531: an explicit, idempotent Apply command so the property panel's Apply affordance
        // performs a real commit instead of acting only as a focus target.
        ApplyCommand = new RelayCommand(Apply);

        // User-requested font-size stepper: the −/+ buttons and the Up/Down keys share one step
        // operation, so touch and keyboard behave identically.
        DecreaseFontSizeCommand = new RelayCommand(() => DecreaseFontSize());
        IncreaseFontSizeCommand = new RelayCommand(() => IncreaseFontSize());

        // User-requested addition: byte/data-rate display unit (Gauge/Bar) and image-file browse.
        DisplayUnitOptions =
        [
            new(DisplayUnit.Auto, "Auto"),
            new(DisplayUnit.AutoKilobytes, "Auto (KB/MB)"),
            new(DisplayUnit.Bytes, "B"),
            new(DisplayUnit.Kilobytes, "KB"),
            new(DisplayUnit.Megabytes, "MB"),
            new(DisplayUnit.Gigabytes, "GB"),
        ];
        BrowseImageCommand = new RelayCommand(BrowseImage, () => CanBrowseImage);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the configuration entry this editor mutates; used by the edit service to keep
    /// the editor for the same selection.</summary>
    internal WidgetConfig Config => _config;

    /// <summary>Gets the edited widget's id.</summary>
    public string WidgetId => _config.Id;

    /// <summary>
    /// Gets the docked picker flyout state (user-requested replacement for inline picker expansion).
    /// The flyout is transient editor state: it is created with the editor and disappears when the
    /// editor closes or the selection changes.
    /// </summary>
    public PropertyPickerFlyoutViewModel Flyout { get; }

    /// <summary>
    /// Gets the command that opens/toggles the icon flyout for the target passed as its parameter
    /// (the <see cref="PropertyPickerTarget"/> name, for example <c>Icon</c> or <c>OnIcon</c>).
    /// </summary>
    public ICommand ToggleIconFlyoutCommand { get; }

    /// <summary>
    /// Gets the command that opens/toggles the colour flyout for the target passed as its parameter
    /// (for example <c>Foreground</c> or <c>OnIconColor</c>).
    /// </summary>
    public ICommand ToggleColorFlyoutCommand { get; }

    /// <summary>Gets the edited widget's config type discriminator.</summary>
    public string WidgetType => _config.Type;

    /// <summary>Gets a human-readable name of the edited widget type for the panel header.</summary>
    public string TypeDisplayName => _config.Type switch
    {
        WidgetTypes.Button => "Button",
        WidgetTypes.Toggle => "Toggle",
        WidgetTypes.Label => "Label",
        WidgetTypes.Gauge => "Gauge",
        WidgetTypes.Slider => "Slider",
        WidgetTypes.Sparkline => "Sparkline",
        WidgetTypes.Clock => "Clock",
        WidgetTypes.ImageButton => "Image Button",
        WidgetTypes.Bar => "Bar",
        WidgetTypes.Image => "Image",
        _ => _config.Type,
    };

    /// <summary>Gets the current validation message, or <see langword="null"/> when the last input was accepted.</summary>
    public string? ValidationError => _validationError;

    /// <summary>Gets a value indicating whether a validation message is shown.</summary>
    public bool HasValidationError => _validationError is not null;

    /// <summary>
    /// Gets the action picker for the selected widget, or <see langword="null"/> when the widget does
    /// not bind an action (RF-043/M2-RF-434). The WPF property panel binds to this and collapses the
    /// action section while it is <see langword="null"/>; the M2 picker is offered for the
    /// action-bearing Button and Toggle widgets.
    /// </summary>
    public ActionPickerViewModel? ActionPicker { get; }

    /// <summary>Gets a value indicating whether an action picker is available for the current selection.</summary>
    public bool HasActionPicker => ActionPicker is not null;

    /// <summary>Gets a value indicating whether the edited widget exposes a gauge/slider/bar range.</summary>
    public bool HasRange => _config is GaugeWidgetConfig or SliderWidgetConfig or BarWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes a sensor key.</summary>
    public bool HasSensor => _config is GaugeWidgetConfig or SparklineWidgetConfig or BarWidgetConfig;

    /// <summary>
    /// Gets the selectable sensor keys for the sensor picker (M5-RF-510/M5-RF-511). The list always
    /// contains the fixed aggregate keys plus any per-core/fan keys discovered from the current
    /// snapshot and, when the widget's configured sensor is unknown, that key as a "(current)" entry
    /// (M5-RF-512); it is never empty. Only meaningful when <see cref="HasSensor"/> is
    /// <see langword="true"/>.
    /// </summary>
    public IReadOnlyList<SensorOption> SensorOptions { get; }

    /// <summary>
    /// Gets or sets the selected sensor option for gauge/sparkline/bar (M5-RF-511). Selecting an
    /// entry writes its string key through the existing <see cref="SensorText"/> validation; an
    /// unknown configured key is preserved and shown, so it is never silently cleared (M5-RF-512).
    /// </summary>
    public SensorOption? SelectedSensor
    {
        get
        {
            var current = ReadSensor();
            return current is null
                ? null
                : SensorOptions.FirstOrDefault(option =>
                    string.Equals(option.Key, current, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (value is null)
            {
                return;
            }

            SensorText = value.Key;
        }
    }

    /// <summary>
    /// Gets the explicit, idempotent Apply command backing the property panel's Apply affordance
    /// (M5-RF-531). It re-projects the committed configuration onto the live widget without a
    /// rebuild and marks the layout dirty; it is safe to invoke repeatedly and never removes the
    /// widget. The Apply button is focusable, so tapping it first commits the focused field through
    /// its <c>LostFocus</c> binding.
    /// </summary>
    public ICommand ApplyCommand { get; }

    /// <summary>
    /// Gets the command backing the Font size field's <b>−</b> stepper (user-requested addition). One
    /// invocation decreases the value by one DIP, clamped to the 8–96 DIP range, applies it to the
    /// live widget immediately, and rewrites the field to the canonical number. At the lower bound
    /// the command is a no-op (the view disables the button through <see cref="CanDecreaseFontSize"/>).
    /// </summary>
    public ICommand DecreaseFontSizeCommand { get; }

    /// <summary>
    /// Gets the command backing the Font size field's <b>+</b> stepper (user-requested addition); see
    /// <see cref="DecreaseFontSizeCommand"/> for the shared behaviour.
    /// </summary>
    public ICommand IncreaseFontSizeCommand { get; }

    /// <summary>
    /// Gets a value indicating whether the font size can be decreased by one step. It is
    /// <see langword="false"/> at the 8 DIP lower bound, which disables the <b>−</b> button; the same
    /// step is ignored when it arrives from the Down arrow key.
    /// </summary>
    public bool CanDecreaseFontSize => CanStepNumericField(nameof(FontSizeText), -1);

    /// <summary>
    /// Gets a value indicating whether the font size can be increased by one step. It is
    /// <see langword="false"/> at the 96 DIP upper bound, which disables the <b>+</b> button; the same
    /// step is ignored when it arrives from the Up arrow key.
    /// </summary>
    public bool CanIncreaseFontSize => CanStepNumericField(nameof(FontSizeText), 1);

    /// <summary>
    /// Gets a value indicating whether a live-edit session is active for the font-size field. While
    /// active, typing previews the clamped value on the live widget on every change without rewriting
    /// the text or adding undo steps beyond the group's first one.
    /// </summary>
    public bool IsLiveEditingFontSize => IsLiveEditingNumericField(nameof(FontSizeText));

    /// <summary>
    /// Gets a value indicating whether a live-edit session is active for <paramref name="field"/>
    /// (user-requested live preview, generalized to every numeric field).
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <returns><see langword="true"/> when the field owns the active session.</returns>
    internal bool IsLiveEditingNumericField(string field) => string.Equals(
        _liveEditProperty,
        field,
        StringComparison.Ordinal);

    /// <summary>
    /// Begins a live-edit session for the font-size field: called when the field gains focus. The
    /// first applied change of the session records the group's single undo point; later changes
    /// preview without recording, so one Undo restores the value from before the whole edit.
    /// </summary>
    public void BeginLiveEdit() => BeginNumericLiveEdit(nameof(FontSizeText), fromFocus: true);

    /// <summary>
    /// Begins a live-edit session for a supported numeric field, preserving the existing single-undo
    /// group policy. A session for a different field is committed first, so moving directly from one
    /// field to another never merges two edits into one undo step.
    /// </summary>
    /// <param name="propertyName">The field's property name; unsupported names are ignored.</param>
    public void BeginLiveEdit(string propertyName) => BeginNumericLiveEdit(propertyName, fromFocus: true);

    /// <summary>
    /// Begins (or continues) the live-edit session for a numeric field. Kept public and WPF-free so
    /// the reusable input/stepper controls can open the same session the typed path uses.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="fromFocus">Whether focus opened the session (typing) or a stepper did.</param>
    public void BeginNumericLiveEdit(string field, bool fromFocus)
    {
        if (GetNumericFieldSpec(field) is null)
        {
            return;
        }

        if (_liveEditProperty is not null
            && !string.Equals(_liveEditProperty, field, StringComparison.Ordinal))
        {
            CommitNumericLiveEdit(_liveEditProperty);
        }

        if (_liveEditProperty is null)
        {
            _liveEditProperty = field;
            _liveEditUndoRecorded = false;
            _liveEditFromFocus = fromFocus;
            _liveEditLastValidText = GetNumericFieldText(field);
        }
        else if (fromFocus)
        {
            // A stepper opened the group first and the field then received focus: keep the one
            // group but let LostFocus/Enter own its end from now on.
            _liveEditFromFocus = true;
        }
    }

    /// <summary>Alias for <see cref="BeginLiveEdit()"/> naming the field explicitly.</summary>
    public void BeginFontSizeLiveEdit() => BeginLiveEdit(nameof(FontSizeText));

    /// <summary>
    /// Ends the active live-edit session (LostFocus/Enter): a valid value is normalized and the text
    /// is rewritten to the canonical format, an empty value clears the override (inherit the theme
    /// default) for the appearance fields, and an invalid value restores the last valid text. The
    /// whole session remains one undo step. Safe to call when no session is active.
    /// </summary>
    public void CommitLiveEdit()
    {
        if (_liveEditProperty is { } active)
        {
            CommitNumericLiveEdit(active);
        }
    }

    /// <summary>
    /// Ends the active live-edit session for a supported field; see <see cref="CommitLiveEdit()"/>.
    /// Unsupported or inactive fields are a no-op.
    /// </summary>
    /// <param name="propertyName">The field's property name.</param>
    public void CommitLiveEdit(string propertyName) => CommitNumericLiveEdit(propertyName);

    /// <summary>Alias for <see cref="CommitLiveEdit()"/> naming the field explicitly.</summary>
    public void CommitFontSizeLiveEdit() => CommitLiveEdit(nameof(FontSizeText));

    /// <summary>
    /// Ends an active live-edit session cleanly, committing the pending value (used when the selection
    /// changes or the editor collapses mid-edit). Safe when no session is active.
    /// </summary>
    public void EndLiveEdit() => CommitLiveEdit();

    /// <summary>
    /// Commits the session for <paramref name="field"/> when it owns the active session. The value is
    /// normalized/clamped and the text is rewritten to its canonical form.
    /// </summary>
    /// <param name="field">The field key.</param>
    public void CommitNumericLiveEdit(string field)
    {
        if (!string.Equals(_liveEditProperty, field, StringComparison.Ordinal))
        {
            return;
        }

        _liveEditProperty = null;
        CommitNumericFieldCore(field);
    }

    /// <summary>
    /// Ends a stepper-opened session on release, so one press/hold is one undo group. A session
    /// opened by keyboard focus is deliberately left open: the typed and stepped changes belong to
    /// the same focus session, exactly as before.
    /// </summary>
    /// <param name="field">The field key.</param>
    public void CommitNumericStepHold(string field)
    {
        if (!string.Equals(_liveEditProperty, field, StringComparison.Ordinal) || _liveEditFromFocus)
        {
            return;
        }

        _liveEditProperty = null;
        CommitNumericFieldCore(field);
    }

    /// <summary>
    /// Applies one stepper step to any numeric field: the value is read from the field when it parses,
    /// otherwise from the last valid/applied value; it is normalized into the field's range, moved by
    /// the field's step (or magnitude step), applied live, and the field is rewritten to canonical
    /// text. At a bound the call is a no-op, which is what disables the button.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="direction"><c>-1</c> or <c>+1</c>.</param>
    /// <returns><see langword="true"/> when the value moved.</returns>
    public bool StepNumericField(string field, int direction)
    {
        if (direction is not (-1 or 1)
            || GetNumericFieldSpec(field) is not { } spec
            || !TryGetNumericBaseValue(field, spec, out var current)
            || !NumericFieldMath.TryStep(current, direction, spec, out var next))
        {
            return false;
        }

        if (_liveEditProperty is not null
            && !string.Equals(_liveEditProperty, field, StringComparison.Ordinal))
        {
            CommitNumericLiveEdit(_liveEditProperty);
        }

        if (!IsLiveEditingNumericField(field))
        {
            BeginNumericLiveEdit(field, fromFocus: false);
        }

        var applied = ApplyNumericValue(field, next);
        var canonical = NumericFieldMath.Format(applied, spec.Decimals);
        WriteNumericFieldText(field, canonical);
        _liveEditLastValidText = canonical;
        ClearNumericFieldError(field);
        NotifyNumericFieldChanged(field);
        return true;
    }

    /// <summary>
    /// Gets whether a step in <paramref name="direction"/> would move the field's value. The button
    /// uses this for its enabled state; a bound therefore disables just the relevant button.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="direction"><c>-1</c> or <c>+1</c>.</param>
    /// <returns><see langword="true"/> when the step is possible.</returns>
    public bool CanStepNumericField(string field, int direction)
    {
        if (direction is not (-1 or 1) || GetNumericFieldSpec(field) is not { } spec)
        {
            return false;
        }

        return TryGetNumericBaseValue(field, spec, out var current)
            && NumericFieldMath.TryStep(current, direction, spec, out _);
    }

    /// <summary>
    /// Gets the current spec (step, range, decimals, sign policy) for a numeric field, or
    /// <see langword="null"/> when the field is not numeric for the current widget. Bounds are
    /// computed from the live state: geometry is bound by the page and current extents, and the
    /// range bounds are bound by the opposite bound and the slider step.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <returns>The field spec.</returns>
    public NumericFieldSpec? GetNumericFieldSpec(string field) => field switch
    {
        nameof(FontSizeText) => new(
            FontSizeStepValue,
            WidgetAppearance.MinFontSize,
            WidgetAppearance.MaxFontSize,
            Decimals: 0,
            AllowNegative: false,
            AllowsEmpty: true),
        nameof(OpacityText) => new(
            0.1,
            WidgetAppearance.MinOpacity,
            WidgetAppearance.MaxOpacity,
            Decimals: 1,
            AllowNegative: false,
            AllowsEmpty: true),
        nameof(XText) => new(
            1,
            0,
            Math.Max(0, _owner.PageWidth - _widget.Width),
            Decimals: 0,
            AllowNegative: false),
        nameof(YText) => new(
            1,
            0,
            Math.Max(0, _owner.PageHeight - _widget.Height),
            Decimals: 0,
            AllowNegative: false),
        nameof(WidthText) => new(
            1,
            EditModeService.MinimumWidgetSize,
            Math.Max(EditModeService.MinimumWidgetSize, _owner.PageWidth - _widget.X),
            Decimals: 0,
            AllowNegative: false),
        nameof(HeightText) => new(
            1,
            EditModeService.MinimumWidgetSize,
            Math.Max(EditModeService.MinimumWidgetSize, _owner.PageHeight - _widget.Y),
            Decimals: 0,
            AllowNegative: false),
        nameof(MinimumText) when HasRange => new(
            Step: 1,
            -ConfigValidator.MaxGaugeMagnitude,
            ReadMaximum() - RangeGap(),
            Decimals: 3,
            AllowNegative: true,
            StepByMagnitude: true),
        nameof(MaximumText) when HasRange => new(
            Step: 1,
            ReadMinimum() + RangeGap(),
            ConfigValidator.MaxGaugeMagnitude,
            Decimals: 3,
            AllowNegative: true,
            StepByMagnitude: true),
        nameof(StepText) when HasStep => new(
            Step: 1,
            Minimum: 0.001,
            Maximum: Math.Max(0.001, _config is SliderWidgetConfig stepSlider
                ? stepSlider.Maximum - stepSlider.Minimum
                : 0.001),
            Decimals: 3,
            AllowNegative: false,
            StepByMagnitude: true,
            FineStep: 0.1),
        nameof(InitialValueText) when HasInitialValue => InitialValueSpec(),
        nameof(WindowText) when HasWindow => new(
            1,
            SparklineWidgetConfig.MinWindowSeconds,
            SparklineWidgetConfig.MaxWindowSeconds,
            Decimals: 3,
            AllowNegative: false),
        nameof(ActionPickerViewModel.LevelText) when _config.Action is VolumeActionConfig => new(
            1,
            ConfigValidator.MinVolumeLevel,
            ConfigValidator.MaxVolumeLevel,
            Decimals: 0,
            AllowNegative: false),
        _ => null,
    };

    /// <summary>Gets the raw text of a numeric field for the reusable input/stepper controls.</summary>
    /// <param name="field">The field key.</param>
    /// <returns>The raw text, or an empty string when the field is unknown.</returns>
    public string GetNumericFieldText(string field) => field switch
    {
        nameof(FontSizeText) => _fontSizeText,
        nameof(OpacityText) => _opacityText,
        nameof(XText) => _xText,
        nameof(YText) => _yText,
        nameof(WidthText) => _widthText,
        nameof(HeightText) => _heightText,
        nameof(MinimumText) => _minimumText,
        nameof(MaximumText) => _maximumText,
        nameof(StepText) => _stepText,
        nameof(InitialValueText) => _initialValueText,
        nameof(WindowText) => _windowText,
        nameof(ActionPickerViewModel.LevelText) => ActionPicker?.LevelText ?? string.Empty,
        _ => string.Empty,
    };

    /// <summary>
    /// Raises the change notification for a field written by another view model (the action picker's
    /// volume level) so the stepper enablement stays in sync without duplicating state.
    /// </summary>
    /// <param name="field">The field key.</param>
    internal void NotifyNumericFieldChanged(string field) => OnPropertyChanged(field);

    /// <summary>
    /// Applies a live numeric text edit when <paramref name="field"/> owns the session. Used by the
    /// action picker's level field, whose text lives on the picker rather than on this editor.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="value">The raw text.</param>
    /// <returns><see langword="true"/> when a live session handled the edit.</returns>
    internal bool TryApplyLiveNumericText(string field, string? value)
    {
        if (!IsLiveEditingNumericField(field))
        {
            return false;
        }

        ApplyLiveNumericText(field, value);
        return true;
    }

    /// <summary>Decreases the font size by one step; see <see cref="StepNumericField"/>.</summary>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public bool DecreaseFontSize() => StepFontSize(-1);

    /// <summary>Increases the font size by one step; see <see cref="StepNumericField"/>.</summary>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public bool IncreaseFontSize() => StepFontSize(1);

    /// <summary>Applies one font-size step; see <see cref="StepNumericField"/>.</summary>
    /// <param name="direction"><c>-1</c> or <c>+1</c>.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public bool StepFontSize(int direction) => StepNumericField(nameof(FontSizeText), direction);

    /// <summary>Gets a value indicating whether the edited widget exposes a slider step.</summary>
    public bool HasStep => _config is SliderWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes a slider initial value.</summary>
    public bool HasInitialValue => _config is SliderWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes a slider orientation.</summary>
    public bool HasOrientation => _config is SliderWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes a history window.</summary>
    public bool HasWindow => _config is SparklineWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes a render style.</summary>
    public bool HasStyle => _config is SparklineWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes toggle persistence options.</summary>
    public bool HasPersistState => _config is ToggleWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes a Clock display mode (M4-RF-432).</summary>
    public bool HasMode => _config is ClockWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes a Clock custom format (M4-RF-432).</summary>
    public bool HasFormat => _config is ClockWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes an ImageButton show-label flag (M4-RF-432).</summary>
    public bool HasShowLabel => _config is ImageButtonWidgetConfig;

    /// <summary>
    /// Gets a value indicating whether the edited widget exposes a show-value flag (M4-RF-432;
    /// user-requested addition for the Sparkline): the Bar meter and the Sparkline value readout.
    /// </summary>
    public bool HasShowValue => _config is BarWidgetConfig or SparklineWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes an Image file path.</summary>
    public bool HasImagePath => _config is ImageWidgetConfig;

    /// <summary>Gets a value indicating whether the edited widget exposes an Image fit mode.</summary>
    public bool HasStretch => _config is ImageWidgetConfig;

    /// <summary>
    /// Gets a value indicating whether the edited widget exposes a byte/data-rate display unit
    /// (user-requested addition). Offered for the byte-capable Gauge, Bar, and Sparkline widgets; the
    /// option is ignored at render time for sensors whose unit is not bytes/bytes-per-second.
    /// </summary>
    public bool HasDisplayUnit => _config is GaugeWidgetConfig or BarWidgetConfig or SparklineWidgetConfig;

    /// <summary>
    /// Gets the selectable byte/data-rate display units, headed by "auto" (the default and the
    /// human-friendly existing behavior) and "auto (KB/MB)" (binary KB below 1024 KB, MB at or above
    /// it). The unknown sentinel is never offered.
    /// </summary>
    public IReadOnlyList<DisplayUnitOption> DisplayUnitOptions { get; }

    /// <summary>
    /// Gets or sets the selected byte/data-rate display unit for a Gauge/Bar/Sparkline widget
    /// (user-requested addition). Selecting an entry commits it to the configuration, projects it
    /// live (the value text re-formats for a byte/rate sensor), and marks the layout dirty. An
    /// unknown configured value is shown and treated as "auto".
    /// </summary>
    public DisplayUnitOption SelectedDisplayUnit
    {
        get
        {
            var unit = ReadDisplayUnit();
            return DisplayUnitOptions.FirstOrDefault(option => option.Unit == unit) ?? DisplayUnitOptions[0];
        }
        set
        {
            if (value is null || !TrySetDisplayUnit(value.Unit))
            {
                return;
            }

            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets the command that opens the image-file browse for the Image widget's path field
    /// (user-requested addition); it is a no-op when no picker seam was supplied or the selected
    /// widget is not an Image.
    /// </summary>
    public ICommand BrowseImageCommand { get; }

    /// <summary>Gets a value indicating whether the image-file browse is available for the current selection.</summary>
    public bool CanBrowseImage => _config is ImageWidgetConfig && _imageFilePicker is not null;

    /// <summary>Gets the selectable image fit modes (the unknown sentinel is never offered).</summary>
    public IReadOnlyList<ImageStretchMode> StretchOptions { get; } =
        [ImageStretchMode.Uniform, ImageStretchMode.Fill, ImageStretchMode.UniformToFill, ImageStretchMode.None];

    /// <summary>Gets the selectable slider orientations (the unknown sentinel is never offered).</summary>
    public IReadOnlyList<SliderOrientation> OrientationOptions { get; } =
        [SliderOrientation.Horizontal, SliderOrientation.Vertical];

    /// <summary>Gets the selectable sparkline styles (the unknown sentinel is never offered).</summary>
    public IReadOnlyList<SparklineStyle> StyleOptions { get; } =
        [SparklineStyle.Line, SparklineStyle.Filled];

    /// <summary>Gets the selectable Clock display presets (the unknown sentinel is never offered).</summary>
    public IReadOnlyList<ClockMode> ModeOptions { get; } =
        [ClockMode.Time24, ClockMode.Time12, ClockMode.DateShort, ClockMode.DateLong, ClockMode.DateTime];

    /// <summary>
    /// Gets the built-in icon catalog offered by the picker (RF-043/M2-RF-435), headed by the
    /// "(none)" entry. Only bundled icons are offered; external packs are out of scope (M3).
    /// </summary>
    public IReadOnlyList<IconOption> IconOptions { get; }

    /// <summary>
    /// Gets a value indicating whether the selected widget supports appearance overrides
    /// (RF-043/M2-RF-436). Every editable built-in widget supports them; unknown types never get an
    /// editor.
    /// </summary>
    public bool SupportsAppearance => _config is not UnknownWidgetConfig;

    /// <summary>
    /// Gets the active theme's appearance defaults used by the editor for fields the widget leaves
    /// inherited (M5-RF-542). Exposed so the editor surfaces theme-aware values instead of blanks and
    /// so the precedence (override → global accent → theme default) stays explicit.
    /// </summary>
    public ThemeAppearanceDefault ThemeDefaults => _themeDefaults;

    /// <summary>Gets the active theme's default font size formatted for display (M5-RF-542).</summary>
    public string ThemeFontSizeText => FormatNumber(_themeDefaults.FontSize);

    /// <summary>Gets the active theme's default opacity formatted for display (M5-RF-542).</summary>
    public string ThemeOpacityText => FormatNumber(_themeDefaults.Opacity);

    /// <summary>
    /// Gets or sets the label text. Commits when at most <see cref="MaxLabelLength"/> characters;
    /// an over-long value is blocked with feedback.
    /// </summary>
    public string LabelText
    {
        get => _labelText;
        set
        {
            var text = value ?? string.Empty;
            if (SetField(ref _labelText, text))
            {
                if (text.Length > MaxLabelLength)
                {
                    SetError($"Label is limited to {MaxLabelLength} characters.");
                    return;
                }

                _config.Label = text.Length == 0 ? null : text;
                CommitConfig();
            }
        }
    }

    /// <summary>Gets or sets the horizontal position in DIPs; clamped into the page by the edit service.</summary>
    public string XText
    {
        get => _xText;
        set
        {
            if (IsLiveEditingNumericField(nameof(XText)))
            {
                ApplyLiveNumericText(nameof(XText), value);
                return;
            }

            var text = value ?? string.Empty;
            if (!SetField(ref _xText, text))
            {
                return;
            }

            if (!TryParseDouble(text, out var x))
            {
                SetError("Position X must be a number.");
                return;
            }

            _owner.MoveWidget(_widget, x, _widget.Y);
            SetField(ref _xText, FormatNumber(_widget.X));
            ClearError();
        }
    }

    /// <summary>Gets or sets the vertical position in DIPs; clamped into the page by the edit service.</summary>
    public string YText
    {
        get => _yText;
        set
        {
            if (IsLiveEditingNumericField(nameof(YText)))
            {
                ApplyLiveNumericText(nameof(YText), value);
                return;
            }

            var text = value ?? string.Empty;
            if (!SetField(ref _yText, text))
            {
                return;
            }

            if (!TryParseDouble(text, out var y))
            {
                SetError("Position Y must be a number.");
                return;
            }

            _owner.MoveWidget(_widget, _widget.X, y);
            SetField(ref _yText, FormatNumber(_widget.Y));
            ClearError();
        }
    }

    /// <summary>Gets or sets the width in DIPs; clamped by the edit service (40 DIP minimum).</summary>
    public string WidthText
    {
        get => _widthText;
        set
        {
            if (IsLiveEditingNumericField(nameof(WidthText)))
            {
                ApplyLiveNumericText(nameof(WidthText), value);
                return;
            }

            var text = value ?? string.Empty;
            if (!SetField(ref _widthText, text))
            {
                return;
            }

            if (!TryParseDouble(text, out var width))
            {
                SetError("Width must be a number.");
                return;
            }

            _owner.ResizeWidget(_widget, width, _widget.Height);
            SetField(ref _widthText, FormatNumber(_widget.Width));
            ClearError();
        }
    }

    /// <summary>Gets or sets the height in DIPs; clamped by the edit service (40 DIP minimum).</summary>
    public string HeightText
    {
        get => _heightText;
        set
        {
            if (IsLiveEditingNumericField(nameof(HeightText)))
            {
                ApplyLiveNumericText(nameof(HeightText), value);
                return;
            }

            var text = value ?? string.Empty;
            if (!SetField(ref _heightText, text))
            {
                return;
            }

            if (!TryParseDouble(text, out var height))
            {
                SetError("Height must be a number.");
                return;
            }

            _owner.ResizeWidget(_widget, _widget.Width, height);
            SetField(ref _heightText, FormatNumber(_widget.Height));
            ClearError();
        }
    }

    /// <summary>Gets or sets the sensor key (gauge/sparkline); a blank value is blocked.</summary>
    public string SensorText
    {
        get => _sensorText;
        set
        {
            var text = value ?? string.Empty;
            if (!SetField(ref _sensorText, text))
            {
                return;
            }

            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                SetError("Sensor is required.");
                return;
            }

            switch (_config)
            {
                case GaugeWidgetConfig gauge:
                    gauge.Sensor = trimmed;
                    break;
                case SparklineWidgetConfig sparkline:
                    sparkline.Sensor = trimmed;
                    break;
                case BarWidgetConfig bar:
                    bar.Sensor = trimmed;
                    break;
                default:
                    return;
            }

            // User-reported fix: switching a Bar/Gauge to a bytes-per-second sensor (net/disk) while
            // its range is still the untouched 0–100 default adopts the unit-aware 0–100 Mbps scale,
            // so the fill is correct immediately instead of only after the next load normalization.
            ApplyRateScaleForSensorChange();
            CommitConfig();
        }
    }

    /// <summary>Gets or sets the range minimum (gauge/slider); the value is validated against the span.</summary>
    public string MinimumText
    {
        get => _minimumText;
        set
        {
            if (IsLiveEditingNumericField(nameof(MinimumText)))
            {
                ApplyLiveNumericText(nameof(MinimumText), value);
                return;
            }

            if (!SetField(ref _minimumText, value ?? string.Empty))
            {
                return;
            }

            if (!TryParseDouble(_minimumText, out var minimum))
            {
                SetError("Minimum must be a number.");
                return;
            }

            if (!TrySetMinimum(minimum))
            {
                return;
            }

            SetField(ref _minimumText, FormatNumber(ReadMinimum()));
        }
    }

    /// <summary>Gets or sets the range maximum (gauge/slider); the value is validated against the span.</summary>
    public string MaximumText
    {
        get => _maximumText;
        set
        {
            if (IsLiveEditingNumericField(nameof(MaximumText)))
            {
                ApplyLiveNumericText(nameof(MaximumText), value);
                return;
            }

            if (!SetField(ref _maximumText, value ?? string.Empty))
            {
                return;
            }

            if (!TryParseDouble(_maximumText, out var maximum))
            {
                SetError("Maximum must be a number.");
                return;
            }

            if (!TrySetMaximum(maximum))
            {
                return;
            }

            SetField(ref _maximumText, FormatNumber(ReadMaximum()));
        }
    }

    /// <summary>Gets or sets the slider step; must be positive and within the range.</summary>
    public string StepText
    {
        get => _stepText;
        set
        {
            if (IsLiveEditingNumericField(nameof(StepText)))
            {
                ApplyLiveNumericText(nameof(StepText), value);
                return;
            }

            if (!SetField(ref _stepText, value ?? string.Empty))
            {
                return;
            }

            if (!TryParseDouble(_stepText, out var step))
            {
                SetError("Step must be a number.");
                return;
            }

            if (_config is not SliderWidgetConfig slider)
            {
                return;
            }

            var span = slider.Maximum - slider.Minimum;
            if (!double.IsFinite(step) || step <= 0 || step > span)
            {
                SetError("Step must be greater than 0 and no larger than the range.");
                return;
            }

            slider.Step = step;
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets the slider's configured initial value (M2-RF-401/M2-RF-433). A finite number is
    /// clamped into <c>[Minimum, Maximum]</c> and snapped to <see cref="Step"/> using the exact rule
    /// <see cref="ConfigValidator.NormalizeSliderValue"/> applies at load time; the normalized value
    /// is committed and reflected back into the field. A non-numeric entry is blocked with feedback
    /// and leaves the configuration untouched. The live value produced by dragging is transient and
    /// is never written here (D-M2-6).
    /// </summary>
    public string InitialValueText
    {
        get => _initialValueText;
        set
        {
            if (IsLiveEditingNumericField(nameof(InitialValueText)))
            {
                ApplyLiveNumericText(nameof(InitialValueText), value);
                return;
            }

            if (!SetField(ref _initialValueText, value ?? string.Empty))
            {
                return;
            }

            if (_config is not SliderWidgetConfig slider)
            {
                return;
            }

            if (!TryParseDouble(_initialValueText, out var parsed))
            {
                SetError("Value must be a number.");
                return;
            }

            var normalized = ConfigValidator.NormalizeSliderValue(
                parsed,
                slider.Minimum,
                slider.Maximum,
                slider.Step);
            slider.Value = normalized;
            CommitConfig();
            SetField(ref _initialValueText, FormatNumber(normalized));
        }
    }

    /// <summary>Gets or sets the sparkline history window in seconds; clamped to the configured bounds.</summary>
    public string WindowText
    {
        get => _windowText;
        set
        {
            if (IsLiveEditingNumericField(nameof(WindowText)))
            {
                ApplyLiveNumericText(nameof(WindowText), value);
                return;
            }

            if (!SetField(ref _windowText, value ?? string.Empty))
            {
                return;
            }

            if (_config is not SparklineWidgetConfig sparkline || !TryParseDouble(_windowText, out var window))
            {
                SetError("Window must be a number.");
                return;
            }

            var clamped = double.IsFinite(window)
                ? Math.Clamp(window, SparklineWidgetConfig.MinWindowSeconds, SparklineWidgetConfig.MaxWindowSeconds)
                : SparklineWidgetConfig.DefaultWindowSeconds;
            sparkline.WindowSeconds = clamped;
            CommitConfig();
            SetField(ref _windowText, FormatNumber(clamped));
        }
    }

    /// <summary>Gets or sets the slider orientation (horizontal/vertical).</summary>
    public SliderOrientation SelectedOrientation
    {
        get => _config is SliderWidgetConfig slider && slider.Orientation == SliderOrientation.Vertical
            ? SliderOrientation.Vertical
            : SliderOrientation.Horizontal;
        set
        {
            if (_config is not SliderWidgetConfig slider || slider.Orientation == value)
            {
                return;
            }

            slider.Orientation = value;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>Gets or sets the sparkline render style (line/filled).</summary>
    public SparklineStyle SelectedStyle
    {
        get => _config is SparklineWidgetConfig sparkline && sparkline.Style == SparklineStyle.Filled
            ? SparklineStyle.Filled
            : SparklineStyle.Line;
        set
        {
            if (_config is not SparklineWidgetConfig sparkline || sparkline.Style == value)
            {
                return;
            }

            sparkline.Style = value;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>Gets or sets whether the toggle persists its state (RF-045/M2-RF-451).</summary>
    public bool PersistState
    {
        get => _config is ToggleWidgetConfig toggle && toggle.PersistState;
        set
        {
            if (_config is not ToggleWidgetConfig toggle || toggle.PersistState == value)
            {
                return;
            }

            toggle.PersistState = value;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>Gets or sets the toggle initial on/off state.</summary>
    public bool InitialOn
    {
        get => _config is ToggleWidgetConfig toggle && toggle.InitialOn;
        set
        {
            if (_config is not ToggleWidgetConfig toggle || toggle.InitialOn == value)
            {
                return;
            }

            toggle.InitialOn = value;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets the Clock display preset (RF-046/M4-RF-432). The unknown sentinel is never
    /// offered and is ignored if assigned; a valid preset is committed to the config, projected
    /// live (the effective format and displayed text recompute), and marks the layout dirty.
    /// </summary>
    public ClockMode SelectedMode
    {
        get => _config is ClockWidgetConfig clock && clock.Mode != ClockMode.Unknown
            ? clock.Mode
            : ClockFormat.DefaultMode;
        set
        {
            if (_config is not ClockWidgetConfig clock || value == ClockMode.Unknown || clock.Mode == value)
            {
                return;
            }

            clock.Mode = value;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets the Clock custom format string (RF-046/M4-RF-432). An empty value clears the
    /// custom format so the <see cref="SelectedMode"/> preset applies. A non-empty value must be
    /// within <see cref="ClockWidgetConfig.MaxFormatLength"/> characters and format without throwing
    /// in the canonical Clock culture (<see cref="ConfigValidator.ValidateClock"/> uses the same
    /// rule); an invalid or over-length value is blocked with feedback and leaves the configuration
    /// untouched. A valid value is committed, projected live, and marks the layout dirty.
    /// </summary>
    public string FormatText
    {
        get => _formatText;
        set
        {
            if (!SetField(ref _formatText, value ?? string.Empty))
            {
                return;
            }

            if (_config is not ClockWidgetConfig clock)
            {
                return;
            }

            var trimmed = _formatText.Trim();
            if (trimmed.Length == 0)
            {
                clock.Format = null;
                CommitConfig();
                return;
            }

            if (trimmed.Length > ClockWidgetConfig.MaxFormatLength)
            {
                SetError($"Format is limited to {ClockWidgetConfig.MaxFormatLength} characters.");
                return;
            }

            if (!ClockFormat.TryCustomFormat(trimmed, _clockCulture))
            {
                SetError("Format is not a valid date/time format for the current culture.");
                return;
            }

            clock.Format = trimmed;
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets whether an ImageButton shows its label beneath the icon (RF-046/M4-RF-432). The
    /// change is committed to the config, projected live, and marks the layout dirty.
    /// </summary>
    public bool ShowLabel
    {
        get => _config is ImageButtonWidgetConfig imageButton && imageButton.ShowLabel;
        set
        {
            if (_config is not ImageButtonWidgetConfig imageButton || imageButton.ShowLabel == value)
            {
                return;
            }

            imageButton.ShowLabel = value;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets whether the widget shows its numeric value (RF-046/M4-RF-432): beside the Bar
    /// meter, or in the Sparkline header (user-requested addition). The change is committed to the
    /// config, projected live, and marks the layout dirty.
    /// </summary>
    public bool ShowValue
    {
        get => _showValue;
        set
        {
            if (_showValue == value)
            {
                return;
            }

            switch (_config)
            {
                case BarWidgetConfig bar:
                    bar.ShowValue = value;
                    break;
                case SparklineWidgetConfig sparkline:
                    sparkline.ShowValue = value;
                    break;
                default:
                    return;
            }

            _showValue = value;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets the Image widget's file path. A blank value clears the path; a non-empty value
    /// must be an absolute path (the same rule <c>ConfigValidator</c> applies), because the path is
    /// untrusted configuration and a relative value must never resolve against the current
    /// directory. A valid value is committed to the config, projected live (the image reloads), and
    /// marks the layout dirty; an invalid value is blocked with feedback.
    /// </summary>
    public string ImagePathText
    {
        get => _imagePathText;
        set
        {
            if (!SetField(ref _imagePathText, value ?? string.Empty))
            {
                return;
            }

            if (_config is not ImageWidgetConfig image)
            {
                return;
            }

            var trimmed = _imagePathText.Trim();
            if (trimmed.Length == 0)
            {
                image.Path = null;
                CommitConfig();
                return;
            }

            if (!IsAbsolutePath(trimmed))
            {
                SetError("Image path must be an absolute file path (for example C:\\Pictures\\logo.png).");
                return;
            }

            image.Path = trimmed;
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets the Image widget's fit mode. The unknown sentinel is never offered and is
    /// ignored if assigned; a valid mode is committed to the config, projected live, and marks the
    /// layout dirty.
    /// </summary>
    public ImageStretchMode SelectedStretch
    {
        get => _config is ImageWidgetConfig image && image.Stretch != ImageStretchMode.Unknown
            ? image.Stretch
            : ImageStretchMode.Uniform;
        set
        {
            if (_config is not ImageWidgetConfig image || value == ImageStretchMode.Unknown || image.Stretch == value)
            {
                return;
            }

            image.Stretch = value;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the edited widget renders a distinct label element that the
    /// optional label icon can replace (user-requested addition): Bar, Gauge, Sparkline, and Slider.
    /// </summary>
    public bool SupportsLabelIcon => _config is BarWidgetConfig or GaugeWidgetConfig or SparklineWidgetConfig or SliderWidgetConfig;

    /// <summary>
    /// Gets the entries offered by the label-icon picker (user-requested addition): "(none)" plus the
    /// built-in icon catalog. Selecting an entry renders that icon instead of the label text; the
    /// label text itself is retained and returns as soon as "(none)" is selected.
    /// </summary>
    public IReadOnlyList<IconOption> LabelIconOptions { get; }

    /// <summary>
    /// Gets or sets the icon rendered in place of the widget's label text (user-requested addition).
    /// Selecting "(none)" clears <see cref="WidgetConfig.LabelIcon"/> and restores the text label; a
    /// chosen catalog entry stores its stable id, is projected live, and marks the layout dirty.
    /// </summary>
    public IconOption SelectedLabelIcon
    {
        get => _selectedLabelIcon;
        set
        {
            var option = value ?? NoneIcon;
            if (string.Equals(_selectedLabelIcon.Id, option.Id, StringComparison.Ordinal))
            {
                return;
            }

            // Same containment as the primary icon picker: only built-in catalog ids may be persisted.
            if (option.Id.Length > 0 && !IconCatalog.TryGet(option.Id, out _))
            {
                SetError("Label icon must be one of the built-in icons.");
                return;
            }

            _selectedLabelIcon = option;
            _config.LabelIcon = option.Id.Length == 0 ? null : option.Id;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the edited widget exposes a distinct on-state icon
    /// (user-requested addition): only the Toggle has on/off states, so only it is offered the picker.
    /// </summary>
    public bool SupportsOnIcon => _config is ToggleWidgetConfig;

    /// <summary>
    /// Gets the entries offered by the on-icon picker (user-requested addition): "(none)" plus the
    /// built-in icon catalog. "(none)" keeps the base icon in both states; a chosen entry is rendered
    /// instead while the toggle is on, coloured with the on-icon colour or the contrasting theme
    /// default.
    /// </summary>
    public IReadOnlyList<IconOption> OnIconOptions { get; }

    /// <summary>
    /// Gets or sets the icon rendered by a Toggle while it is on (user-requested addition). Selecting
    /// "(none)" clears <see cref="ToggleWidgetConfig.OnIcon"/> so the base icon shows in both states;
    /// a chosen catalog entry stores its stable id, is projected live, and marks the layout dirty.
    /// </summary>
    public IconOption SelectedOnIcon
    {
        get => _selectedOnIcon;
        set
        {
            var option = value ?? NoneIcon;
            if (string.Equals(_selectedOnIcon.Id, option.Id, StringComparison.Ordinal))
            {
                return;
            }

            // Same containment as the other icon pickers: only built-in catalog ids may be persisted.
            if (option.Id.Length > 0 && !IconCatalog.TryGet(option.Id, out _))
            {
                SetError("On icon must be one of the built-in icons.");
                return;
            }

            if (_config is not ToggleWidgetConfig toggle)
            {
                return;
            }

            _selectedOnIcon = option;
            toggle.OnIcon = option.Id.Length == 0 ? null : option.Id;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets the selected built-in icon (RF-043/M2-RF-435). Selecting "(none)" clears the
    /// icon; a chosen catalog entry stores its stable id. The change is committed to the config,
    /// projected live, and marks the layout dirty.
    /// </summary>
    public IconOption SelectedIcon
    {
        get => _selectedIcon;
        set
        {
            var option = value ?? NoneIcon;
            if (string.Equals(_selectedIcon.Id, option.Id, StringComparison.Ordinal))
            {
                return;
            }

            // RF-081/M4-RNF-145: only built-in catalog icons (or "none") may be persisted; a
            // programmatic/hostile option id is rejected so no external image path can enter config.
            if (option.Id.Length > 0 && !IconCatalog.TryGet(option.Id, out _))
            {
                SetError("Icon must be one of the built-in icons.");
                return;
            }

            _selectedIcon = option;
            _config.Icon = option.Id.Length == 0 ? null : option.Id;
            OnPropertyChanged();
            CommitConfig();
        }
    }

    /// <summary>
    /// Gets or sets the foreground color override (RF-043/M2-RF-436). While no override exists the
    /// field shows the active theme's inherited default and a value equal to it (or empty) is kept
    /// inherited rather than pinned (M5-RF-543); a valid different <c>#RRGGBB</c>/<c>#AARRGGBB</c>
    /// value is normalized and committed; an empty value clears the override; anything else is
    /// blocked with feedback and leaves the configuration untouched.
    /// </summary>
    public string ForegroundText
    {
        get => _foregroundText;
        set => ApplyColor(ref _foregroundText, value, () => _config.Foreground, _themeDefaults.Foreground, v => _config.Foreground = v);
    }

    /// <summary>Gets or sets the background color override; see <see cref="ForegroundText"/> for the rules.</summary>
    public string BackgroundText
    {
        get => _backgroundText;
        set => ApplyColor(ref _backgroundText, value, () => _config.Background, _themeDefaults.Background, v => _config.Background = v);
    }

    /// <summary>Gets or sets the accent color override; see <see cref="ForegroundText"/> for the rules.</summary>
    public string AccentText
    {
        get => _accentText;
        set => ApplyColor(ref _accentText, value, () => _config.Accent, _themeDefaults.Accent, v => _config.Accent = v);
    }

    /// <summary>
    /// Gets a value indicating whether the edited widget exposes the optional on-state icon colour
    /// (user-requested addition): only the Toggle has a distinct on state.
    /// </summary>
    public bool SupportsOnIconColor => _config is ToggleWidgetConfig;

    /// <summary>
    /// Gets or sets the optional on-state icon colour (user-requested addition). An empty value
    /// clears <see cref="ToggleWidgetConfig.OnIconColor"/> so the contrasting theme default applies;
    /// a valid <c>#RRGGBB</c>/<c>#AARRGGBB</c> value is normalized and committed.
    /// </summary>
    public string OnIconColorText
    {
        get => _onIconColorText;
        set => ApplyColor(
            ref _onIconColorText,
            value,
            () => (_config as ToggleWidgetConfig)?.OnIconColor,
            string.Empty,
            v =>
            {
                if (_config is ToggleWidgetConfig toggle)
                {
                    toggle.OnIconColor = v;
                }
            });
    }

    /// <summary>Gets a value indicating whether the foreground uses the inherited theme default.</summary>
    public bool IsForegroundInherited => _config.Foreground is null;

    /// <summary>Gets a value indicating whether the background uses the inherited theme default.</summary>
    public bool IsBackgroundInherited => _config.Background is null;

    /// <summary>Gets a value indicating whether the accent uses the inherited theme default.</summary>
    public bool IsAccentInherited => _config.Accent is null;

    /// <summary>Gets a value indicating whether the on-icon colour uses the contrasting theme default.</summary>
    public bool IsOnIconColorInherited => _config is not ToggleWidgetConfig { OnIconColor: not null };

    /// <summary>
    /// Gets or sets the font-size override in DIPs (RF-043/M2-RF-436). While no override exists the
    /// field shows the active theme's inherited default and a value equal to it (or empty) is kept
    /// inherited rather than pinned (M5-RF-543); a finite number is clamped into <c>8–96 DIP</c> and
    /// committed; an empty value clears the override; a non-numeric entry is blocked with feedback
    /// and leaves the configuration untouched.
    /// </summary>
    public string FontSizeText
    {
        get => _fontSizeText;
        set
        {
            // User-requested live preview: while the field is focused, keep exactly the text being
            // typed and preview the clamped value on the live widget (no rewrite, no extra undo step).
            if (IsLiveEditingNumericField(nameof(FontSizeText)))
            {
                ApplyLiveNumericText(nameof(FontSizeText), value);
                return;
            }

            var text = (value ?? string.Empty).Trim();

            // M5-RF-543: a theme default is a true default. While the field is inherited it is
            // displayed (and written back on LostFocus) as the theme default; treat that text — or a
            // clear — as "still inherited" so the field is never pinned to the current theme.
            if (_config.FontSize is null
                && (text.Length == 0 || string.Equals(text, ThemeFontSizeText, StringComparison.Ordinal)))
            {
                if (SetField(ref _fontSizeText, ThemeFontSizeText))
                {
                    ClearError();
                }

                return;
            }

            if (!SetField(ref _fontSizeText, value ?? string.Empty))
            {
                return;
            }

            var trimmed = _fontSizeText.Trim();
            if (trimmed.Length == 0)
            {
                _config.FontSize = null;
                CommitConfig();
                SetField(ref _fontSizeText, ThemeFontSizeText);
                return;
            }

            if (!TryParseDouble(trimmed, out var parsed))
            {
                SetError("Font size must be a number.");
                return;
            }

            var normalized = WidgetAppearance.NormalizeFontSize(parsed);
            _config.FontSize = normalized;
            CommitConfig();
            SetField(ref _fontSizeText, normalized is double fontSize ? FormatNumber(fontSize) : string.Empty);
        }
    }

    /// <summary>
    /// Previews a numeric keystroke while its field is focused: stores exactly the typed text (never
    /// the clamped/canonical value), applies a valid number clamped to the field's range to the live
    /// widget, and records the session's single undo point on the first applied change. Empty and
    /// non-numeric intermediate values change nothing and surface the inline error, which clears as
    /// soon as the input is valid again.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="value">The raw text typed into the field.</param>
    private void ApplyLiveNumericText(string field, string? value)
    {
        if (GetNumericFieldSpec(field) is not { } spec)
        {
            return;
        }

        var text = value ?? string.Empty;
        WriteNumericFieldText(field, text);

        var trimmed = text.Trim();
        if (trimmed.Length == 0 || !NumericFieldMath.TryParse(trimmed, out var parsed))
        {
            // Keep the user's text so deleting/retyping is never fought by a rewrite.
            SetNumericFieldError(field, NumberMessage(field));
            return;
        }

        ApplyNumericValue(field, NumericFieldMath.Normalize(parsed, spec));
        _liveEditLastValidText = text;
        ClearNumericFieldError(field);
    }

    /// <summary>
    /// Ends a numeric live session: normalizes and rewrites a valid value to the canonical format,
    /// clears an appearance override for an empty value, and restores the last valid text for an
    /// invalid value. The session's undo group is completed (never doubled).
    /// </summary>
    /// <param name="field">The field key.</param>
    private void CommitNumericFieldCore(string field)
    {
        if (GetNumericFieldSpec(field) is not { } spec)
        {
            return;
        }

        var trimmed = GetNumericFieldText(field).Trim();

        // M5-RF-543: a focus/clear/blur on an inherited appearance field with no applied preview is
        // not an edit. Keep the theme default inherited (no pinned override, no dirty state).
        if (spec.AllowsEmpty && IsInheritedAppearanceText(field, trimmed))
        {
            WriteNumericFieldText(field, ThemeFieldText(field));
            ClearNumericFieldError(field);
            return;
        }

        if (trimmed.Length == 0)
        {
            if (spec.AllowsEmpty)
            {
                ClearAppearanceOverride(field);
                WriteNumericFieldText(field, ThemeFieldText(field));
                ClearNumericFieldError(field);
                return;
            }

            SetNumericFieldError(field, NumberMessage(field));
            RestoreLastValidText(field, spec);
            return;
        }

        if (!NumericFieldMath.TryParse(trimmed, out var parsed))
        {
            SetNumericFieldError(field, NumberMessage(field));
            RestoreLastValidText(field, spec);
            return;
        }

        var normalized = NumericFieldMath.Normalize(parsed, spec);
        var applied = ApplyNumericValue(field, normalized);
        var canonical = NumericFieldMath.Format(applied, spec.Decimals);
        WriteNumericFieldText(field, canonical);
        _liveEditLastValidText = canonical;
        ClearNumericFieldError(field);
    }

    /// <summary>
    /// Applies a numeric value to the live widget through the existing edit path. The session's first
    /// change records the undo point and later changes only move the history's current snapshot, so
    /// the whole edit stays one undo step. Returns the value actually applied (clamped, rounded, or
    /// snapped, for example a slider value snapped to its step).
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="value">The normalized candidate value.</param>
    /// <returns>The applied value.</returns>
    private double ApplyNumericValue(string field, double value)
    {
        var recordUndo = !_liveEditUndoRecorded;

        switch (field)
        {
            case nameof(FontSizeText):
            {
                var normalized = WidgetAppearance.NormalizeFontSize(value) ?? WidgetAppearance.MinFontSize;
                _config.FontSize = normalized;
                MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                return normalized;
            }

            case nameof(OpacityText):
            {
                var normalized = WidgetAppearance.NormalizeOpacity(value) ?? WidgetAppearance.MinOpacity;
                _config.Opacity = normalized;
                MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                return normalized;
            }

            case nameof(XText):
                MarkLiveUndo(recordUndo, _owner.MoveWidgetLive(_widget, value, _widget.Y, recordUndo));
                return _widget.X;

            case nameof(YText):
                MarkLiveUndo(recordUndo, _owner.MoveWidgetLive(_widget, _widget.X, value, recordUndo));
                return _widget.Y;

            case nameof(WidthText):
                MarkLiveUndo(recordUndo, _owner.ResizeWidgetLive(_widget, value, _widget.Height, recordUndo));
                return _widget.Width;

            case nameof(HeightText):
                MarkLiveUndo(recordUndo, _owner.ResizeWidgetLive(_widget, _widget.Width, value, recordUndo));
                return _widget.Height;

            case nameof(MinimumText):
                if (TrySetMinimum(value, commit: false))
                {
                    MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                }

                return ReadMinimum();

            case nameof(MaximumText):
                if (TrySetMaximum(value, commit: false))
                {
                    MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                }

                return ReadMaximum();

            case nameof(StepText):
                if (_config is SliderWidgetConfig stepSlider)
                {
                    var span = stepSlider.Maximum - stepSlider.Minimum;
                    stepSlider.Step = Math.Clamp(value, 0.001, Math.Max(0.001, span));
                    MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                    return stepSlider.Step;
                }

                return value;

            case nameof(InitialValueText):
                if (_config is SliderWidgetConfig valueSlider)
                {
                    valueSlider.Value = ConfigValidator.NormalizeSliderValue(
                        value,
                        valueSlider.Minimum,
                        valueSlider.Maximum,
                        valueSlider.Step);
                    MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                    return valueSlider.Value;
                }

                return value;

            case nameof(WindowText):
                if (_config is SparklineWidgetConfig sparkline)
                {
                    sparkline.WindowSeconds = Math.Clamp(
                        value,
                        SparklineWidgetConfig.MinWindowSeconds,
                        SparklineWidgetConfig.MaxWindowSeconds);
                    MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                    return sparkline.WindowSeconds;
                }

                return value;

            case nameof(ActionPickerViewModel.LevelText):
                if (_config.Action is VolumeActionConfig volume)
                {
                    volume.Level = Math.Clamp(
                        (int)Math.Round(value, MidpointRounding.AwayFromZero),
                        ConfigValidator.MinVolumeLevel,
                        ConfigValidator.MaxVolumeLevel);
                    MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                    return volume.Level;
                }

                return value;

            default:
                return value;
        }
    }

    /// <summary>Marks the session's undo point as recorded after its first real change.</summary>
    /// <param name="recordUndo">Whether the call was allowed to record.</param>
    /// <param name="recorded">Whether the edit service actually recorded a new step.</param>
    private void MarkLiveUndo(bool recordUndo, bool recorded)
    {
        if (recordUndo && recorded)
        {
            _liveEditUndoRecorded = true;
        }
    }

    /// <summary>
    /// Reads the value a stepper step starts from: the field's text when it parses, otherwise the
    /// last valid text of the active session, otherwise the applied value (never a bare default).
    /// The result is normalized into the field's range so a typed "120" steps from the applied bound.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="spec">The field spec.</param>
    /// <param name="value">The normalized base value.</param>
    /// <returns><see langword="true"/> when a finite base value exists.</returns>
    private bool TryGetNumericBaseValue(string field, NumericFieldSpec spec, out double value)
    {
        if (NumericFieldMath.TryParse(GetNumericFieldText(field), out var typed))
        {
            value = NumericFieldMath.Normalize(typed, spec);
            return true;
        }

        if (IsLiveEditingNumericField(field)
            && NumericFieldMath.TryParse(_liveEditLastValidText, out var lastValid))
        {
            value = NumericFieldMath.Normalize(lastValid, spec);
            return true;
        }

        value = NumericFieldMath.Normalize(GetAppliedNumericValue(field), spec);
        return double.IsFinite(value);
    }

    /// <summary>Gets the value currently applied to the configuration or widget for a numeric field.</summary>
    /// <param name="field">The field key.</param>
    /// <returns>The applied value.</returns>
    private double GetAppliedNumericValue(string field) => field switch
    {
        nameof(FontSizeText) => _config.FontSize ?? _themeDefaults.FontSize,
        nameof(OpacityText) => _config.Opacity ?? _themeDefaults.Opacity,
        nameof(XText) => _widget.X,
        nameof(YText) => _widget.Y,
        nameof(WidthText) => _widget.Width,
        nameof(HeightText) => _widget.Height,
        nameof(MinimumText) => ReadMinimum(),
        nameof(MaximumText) => ReadMaximum(),
        nameof(StepText) => ReadStep(),
        nameof(InitialValueText) => ReadValue(),
        nameof(WindowText) => ReadWindow(),
        nameof(ActionPickerViewModel.LevelText) => _config.Action is VolumeActionConfig volume
            ? volume.Level
            : VolumeActionConfig.DefaultLevel,
        _ => 0,
    };

    /// <summary>Writes a numeric field's raw text without running its validation/commit setter.</summary>
    /// <param name="field">The field key.</param>
    /// <param name="text">The text to display.</param>
    private void WriteNumericFieldText(string field, string text)
    {
        switch (field)
        {
            case nameof(FontSizeText):
                SetField(ref _fontSizeText, text, nameof(FontSizeText));
                break;
            case nameof(OpacityText):
                SetField(ref _opacityText, text, nameof(OpacityText));
                break;
            case nameof(XText):
                SetField(ref _xText, text, nameof(XText));
                break;
            case nameof(YText):
                SetField(ref _yText, text, nameof(YText));
                break;
            case nameof(WidthText):
                SetField(ref _widthText, text, nameof(WidthText));
                break;
            case nameof(HeightText):
                SetField(ref _heightText, text, nameof(HeightText));
                break;
            case nameof(MinimumText):
                SetField(ref _minimumText, text, nameof(MinimumText));
                break;
            case nameof(MaximumText):
                SetField(ref _maximumText, text, nameof(MaximumText));
                break;
            case nameof(StepText):
                SetField(ref _stepText, text, nameof(StepText));
                break;
            case nameof(InitialValueText):
                SetField(ref _initialValueText, text, nameof(InitialValueText));
                break;
            case nameof(WindowText):
                SetField(ref _windowText, text, nameof(WindowText));
                break;
            case nameof(ActionPickerViewModel.LevelText):
                ActionPicker?.SetLevelText(text);
                break;
        }
    }

    /// <summary>Restores the last valid text after an invalid commit, or the applied value's text.</summary>
    /// <param name="field">The field key.</param>
    /// <param name="spec">The field spec.</param>
    private void RestoreLastValidText(string field, NumericFieldSpec spec)
    {
        var text = _liveEditLastValidText;
        if (!NumericFieldMath.TryParse(text, out _))
        {
            text = NumericFieldMath.Format(GetAppliedNumericValue(field), spec.Decimals);
        }

        WriteNumericFieldText(field, text);
    }

    /// <summary>
    /// Gets whether an appearance field still shows its inherited theme value (or is empty) and has no
    /// explicit override, which must stay inherited rather than be pinned by a focus/blur.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="text">The trimmed field text.</param>
    /// <returns><see langword="true"/> when the field is still inherited.</returns>
    private bool IsInheritedAppearanceText(string field, string text) => field switch
    {
        nameof(FontSizeText) => _config.FontSize is null
            && (text.Length == 0 || string.Equals(text, ThemeFontSizeText, StringComparison.Ordinal)),
        nameof(OpacityText) => _config.Opacity is null
            && (text.Length == 0 || string.Equals(text, ThemeOpacityText, StringComparison.Ordinal)),
        _ => false,
    };

    /// <summary>Clears an appearance field's explicit override so the theme default applies.</summary>
    /// <param name="field">The field key.</param>
    private void ClearAppearanceOverride(string field)
    {
        var recordUndo = !_liveEditUndoRecorded;
        switch (field)
        {
            case nameof(FontSizeText):
                _config.FontSize = null;
                MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                break;
            case nameof(OpacityText):
                _config.Opacity = null;
                MarkLiveUndo(recordUndo, _owner.ApplyPropertyEdit(_widget, _config, recordUndo));
                break;
        }
    }

    /// <summary>Gets the display text of an appearance field's inherited theme default.</summary>
    /// <param name="field">The field key.</param>
    /// <returns>The theme default text, or an empty string for other fields.</returns>
    private string ThemeFieldText(string field) => field switch
    {
        nameof(FontSizeText) => ThemeFontSizeText,
        nameof(OpacityText) => ThemeOpacityText,
        _ => string.Empty,
    };

    /// <summary>Gets the inline validation message for a numeric field.</summary>
    /// <param name="field">The field key.</param>
    /// <returns>The message.</returns>
    private static string NumberMessage(string field) => field switch
    {
        nameof(FontSizeText) => "Font size must be a number.",
        nameof(OpacityText) => "Opacity must be a number.",
        nameof(XText) => "Position X must be a number.",
        nameof(YText) => "Position Y must be a number.",
        nameof(WidthText) => "Width must be a number.",
        nameof(HeightText) => "Height must be a number.",
        nameof(MinimumText) => "Minimum must be a number.",
        nameof(MaximumText) => "Maximum must be a number.",
        nameof(StepText) => "Step must be a number.",
        nameof(InitialValueText) => "Value must be a number.",
        nameof(WindowText) => "Window must be a number.",
        nameof(ActionPickerViewModel.LevelText) => "Level must be a whole number between 0 and 100.",
        _ => "Value must be a number.",
    };

    /// <summary>
    /// Gets the smallest legal distance between the minimum and maximum for the current range widget:
    /// the gauge's validator span, the slider's step, or a minimal positive span for the bar.
    /// </summary>
    /// <returns>The required span.</returns>
    private double RangeGap() => _config switch
    {
        GaugeWidgetConfig => ConfigValidator.MinGaugeSpan,
        SliderWidgetConfig slider => Math.Max(0.001, slider.Step),
        _ => 0.001,
    };

    /// <summary>Builds the spec for the slider's initial-value field (range and step from the slider).</summary>
    /// <returns>The field spec.</returns>
    private NumericFieldSpec InitialValueSpec()
    {
        if (_config is not SliderWidgetConfig slider)
        {
            return new NumericFieldSpec(1, 0, 1, 3, AllowNegative: false);
        }

        return new NumericFieldSpec(
            Math.Max(0.001, slider.Step),
            slider.Minimum,
            slider.Maximum,
            Decimals: 3,
            AllowNegative: slider.Minimum < 0);
    }

    /// <summary>
    /// Gets or sets the opacity override in <c>[0,1]</c> (RF-043/M2-RF-436). While no override exists
    /// the field shows the active theme's inherited default and a value equal to it (or empty) is
    /// kept inherited rather than pinned (M5-RF-543); a finite number is clamped into
    /// <c>0.0–1.0</c> and committed; an empty value clears the override; a non-numeric entry is
    /// blocked with feedback and leaves the configuration untouched.
    /// </summary>
    public string OpacityText
    {
        get => _opacityText;
        set
        {
            if (IsLiveEditingNumericField(nameof(OpacityText)))
            {
                ApplyLiveNumericText(nameof(OpacityText), value);
                return;
            }

            var text = (value ?? string.Empty).Trim();

            // M5-RF-543: see FontSizeText — an inherited field that still shows the theme default
            // (or is cleared) must not become an explicit override.
            if (_config.Opacity is null
                && (text.Length == 0 || string.Equals(text, ThemeOpacityText, StringComparison.Ordinal)))
            {
                if (SetField(ref _opacityText, ThemeOpacityText))
                {
                    ClearError();
                }

                return;
            }

            if (!SetField(ref _opacityText, value ?? string.Empty))
            {
                return;
            }

            var trimmed = _opacityText.Trim();
            if (trimmed.Length == 0)
            {
                _config.Opacity = null;
                CommitConfig();
                SetField(ref _opacityText, ThemeOpacityText);
                return;
            }

            if (!TryParseDouble(trimmed, out var parsed))
            {
                SetError("Opacity must be a number.");
                return;
            }

            var normalized = WidgetAppearance.NormalizeOpacity(parsed);
            _config.Opacity = normalized;
            CommitConfig();
            SetField(ref _opacityText, normalized is double opacity ? FormatNumber(opacity) : string.Empty);
        }
    }

    private void ApplyColor(
        ref string field,
        string? value,
        Func<string?> currentOverride,
        string themeDefault,
        Action<string?> apply,
        [CallerMemberName] string? propertyName = null)
    {
        ApplyColorCore(ref field, value, currentOverride, themeDefault, apply, propertyName);

        // Raise unconditionally: the picker's one-way IsInherited binding must re-assert when a
        // selection equals the theme default (the value stays inherited even though the local DP
        // was set), not only when the inherited flag flips.
        NotifyColorInheritance(propertyName);
    }

    /// <summary>
    /// Raises the <c>Is...Inherited</c> companion of a colour field so the inline picker's collapsed
    /// header switches between the hex value and "Inherited" without a rebuild.
    /// </summary>
    /// <param name="propertyName">The colour property name (for example <c>ForegroundText</c>).</param>
    private void NotifyColorInheritance(string? propertyName)
    {
        const string suffix = "Text";
        if (string.IsNullOrEmpty(propertyName)
            || !propertyName.EndsWith(suffix, StringComparison.Ordinal)
            || propertyName.Length == suffix.Length)
        {
            return;
        }

        OnPropertyChanged("Is" + propertyName[..^suffix.Length] + "Inherited");
    }

    private void ApplyColorCore(
        ref string field,
        string? value,
        Func<string?> currentOverride,
        string themeDefault,
        Action<string?> apply,
        string? propertyName)
    {
        var text = (value ?? string.Empty).Trim();

        // M5-RF-543: a theme default is a true default. While no override exists the field displays
        // (and writes back on LostFocus) the inherited theme default; treating that text — or a
        // clear — as a value would pin the field and defeat a later theme switch.
        if (currentOverride() is null
            && (text.Length == 0 || string.Equals(text, themeDefault, StringComparison.OrdinalIgnoreCase)))
        {
            // Only touch state when the field actually moved to the inherited default; a pure
            // focus+blur that writes the same text back is a no-op (it must not clear unrelated
            // feedback).
            if (SetField(ref field, themeDefault, propertyName))
            {
                ClearError();
            }

            return;
        }

        if (!SetField(ref field, text, propertyName))
        {
            return;
        }

        if (text.Length == 0)
        {
            apply(null);
            CommitConfig();
            SetField(ref field, themeDefault, propertyName);
            return;
        }

        if (!WidgetAppearance.TryNormalizeColor(text, out var normalized) || normalized is null)
        {
            SetError("Color must be #RRGGBB or #AARRGGBB.");
            return;
        }

        apply(normalized);
        SetField(ref field, normalized, propertyName);
        CommitConfig();
    }

    private bool TrySetMinimum(double minimum, bool commit = true)
    {
        if (!IsFiniteMagnitude(minimum))
        {
            SetError("Minimum must be a finite number.");
            return false;
        }

        switch (_config)
        {
            case GaugeWidgetConfig gauge:
                if (gauge.Maximum - minimum < ConfigValidator.MinGaugeSpan)
                {
                    SetError($"Minimum must be at least {ConfigValidator.MinGaugeSpan} below the maximum.");
                    return false;
                }

                gauge.Minimum = minimum;
                break;
            case SliderWidgetConfig slider:
                var span = slider.Maximum - minimum;
                if (span <= 0)
                {
                    SetError("Minimum must be less than the maximum.");
                    return false;
                }

                if (slider.Step > span)
                {
                    SetError("Minimum must leave at least one step of range.");
                    return false;
                }

                slider.Minimum = minimum;
                break;
            case BarWidgetConfig bar:
                if (bar.Maximum - minimum <= 0)
                {
                    SetError("Minimum must be less than the maximum.");
                    return false;
                }

                bar.Minimum = minimum;
                break;
            default:
                return false;
        }

        // An explicit range edit is the user's scale; the auto-adopted rate scale must never undo it.
        _rateScaleAdopted = false;
        if (commit)
        {
            CommitConfig();
        }

        return true;
    }

    private bool TrySetMaximum(double maximum, bool commit = true)
    {
        if (!IsFiniteMagnitude(maximum))
        {
            SetError("Maximum must be a finite number.");
            return false;
        }

        switch (_config)
        {
            case GaugeWidgetConfig gauge:
                if (maximum - gauge.Minimum < ConfigValidator.MinGaugeSpan)
                {
                    SetError($"Maximum must be at least {ConfigValidator.MinGaugeSpan} above the minimum.");
                    return false;
                }

                gauge.Maximum = maximum;
                break;
            case SliderWidgetConfig slider:
                var span = maximum - slider.Minimum;
                if (span <= 0)
                {
                    SetError("Maximum must be greater than the minimum.");
                    return false;
                }

                if (slider.Step > span)
                {
                    SetError("Maximum must leave at least one step of range.");
                    return false;
                }

                slider.Maximum = maximum;
                break;
            case BarWidgetConfig bar:
                if (maximum - bar.Minimum <= 0)
                {
                    SetError("Maximum must be greater than the minimum.");
                    return false;
                }

                bar.Maximum = maximum;
                break;
            default:
                return false;
        }

        // An explicit range edit is the user's scale; the auto-adopted rate scale must never undo it.
        _rateScaleAdopted = false;
        if (commit)
        {
            CommitConfig();
        }

        return true;
    }

    /// <summary>
    /// Applies the unit-aware rate scale after a sensor change (user-reported fix): a Bar/Gauge bound
    /// to a bytes-per-second sensor (<c>net.up</c>/<c>net.down</c>/<c>disk.read</c>/<c>disk.write</c>)
    /// whose range is still the untouched 0–100 default adopts 0–12,500,000 B/s (100 Mbps) — the same
    /// silent normalization <see cref="ConfigValidator"/> applies at load time — and the Min/Max
    /// fields are refreshed so the editor and the persisted file agree immediately. An explicit range
    /// is never rewritten. The mirror case (switching away from the rate sensor after the scale was
    /// auto-adopted and never edited) restores the 0–100 default so a percent/°C/RPM bar is not left
    /// with a 100 Mbps scale; a range the user typed always wins.
    /// </summary>
    private void ApplyRateScaleForSensorChange()
    {
        if (_config is not (GaugeWidgetConfig or BarWidgetConfig))
        {
            return;
        }

        var minimum = ReadMinimum();
        var maximum = ReadMaximum();
        var sensor = ReadSensor();

        if (ConfigValidator.ShouldUseRateDefaultRange(sensor, minimum, maximum))
        {
            SetRange(ConfigValidator.DefaultRateMinimum, ConfigValidator.DefaultRateMaximum);
            _rateScaleAdopted = true;
        }
        else if (_rateScaleAdopted
            && !ConfigValidator.IsRateSensor(sensor)
            && minimum.Equals(ConfigValidator.DefaultRateMinimum)
            && maximum.Equals(ConfigValidator.DefaultRateMaximum))
        {
            SetRange(ConfigValidator.DefaultRangeMinimum, ConfigValidator.DefaultRangeMaximum);
            _rateScaleAdopted = false;
        }
        else
        {
            _rateScaleAdopted = false;
        }

        if (HasRange)
        {
            SetField(ref _minimumText, FormatNumber(ReadMinimum()), nameof(MinimumText));
            SetField(ref _maximumText, FormatNumber(ReadMaximum()), nameof(MaximumText));
        }
    }

    /// <summary>Writes a bar/gauge display range; a no-op for every other widget type.</summary>
    private void SetRange(double minimum, double maximum)
    {
        switch (_config)
        {
            case GaugeWidgetConfig gauge:
                gauge.Minimum = minimum;
                gauge.Maximum = maximum;
                break;
            case BarWidgetConfig bar:
                bar.Minimum = minimum;
                bar.Maximum = maximum;
                break;
        }
    }

    private void CommitConfig()
    {
        ClearError();
        _owner.ApplyPropertyEdit(_widget, _config);
    }

    /// <summary>
    /// Projects an accepted action-picker edit (M2-RF-434) onto the live widget and marks the layout
    /// dirty without touching this editor's own validation message, which the picker owns. The
    /// action is already validated by the picker with the config-validator rules.
    /// </summary>
    internal void ApplyActionEdit() => _owner.ApplyPropertyEdit(_widget, _config);

    /// <summary>
    /// Explicit Apply commit (M5-RF-531): clears any stale feedback and re-projects the current
    /// configuration onto the live widget, marking the layout dirty so the existing single-writer
    /// save persists it. Idempotent and never removes the widget; the edit service re-checks edit
    /// mode and containment.
    /// </summary>
    private void Apply()
    {
        ClearError();
        _owner.ApplyPropertyEdit(_widget, _config);
    }

    private string? ReadSensor() => _config switch
    {
        GaugeWidgetConfig gauge => gauge.Sensor,
        SparklineWidgetConfig sparkline => sparkline.Sensor,
        BarWidgetConfig bar => bar.Sensor,
        _ => null,
    };

    private DisplayUnit ReadDisplayUnit() => _config switch
    {
        GaugeWidgetConfig gauge when gauge.DisplayUnit != DisplayUnit.Unknown => gauge.DisplayUnit,
        BarWidgetConfig bar when bar.DisplayUnit != DisplayUnit.Unknown => bar.DisplayUnit,
        SparklineWidgetConfig sparkline when sparkline.DisplayUnit != DisplayUnit.Unknown => sparkline.DisplayUnit,
        _ => DisplayUnit.Auto,
    };

    /// <summary>
    /// Writes a valid display unit to the configuration; the unknown sentinel is never committed.
    /// Returns <see langword="false"/> (no mutation) when the widget does not support the option or
    /// the value is unchanged.
    /// </summary>
    private bool TrySetDisplayUnit(DisplayUnit unit)
    {
        if (unit == DisplayUnit.Unknown)
        {
            return false;
        }

        switch (_config)
        {
            case GaugeWidgetConfig gauge:
                if (gauge.DisplayUnit == unit)
                {
                    return false;
                }

                gauge.DisplayUnit = unit;
                return true;
            case BarWidgetConfig bar:
                if (bar.DisplayUnit == unit)
                {
                    return false;
                }

                bar.DisplayUnit = unit;
                return true;
            case SparklineWidgetConfig sparkline:
                if (sparkline.DisplayUnit == unit)
                {
                    return false;
                }

                sparkline.DisplayUnit = unit;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Opens the image picker and, on a non-empty selection, fills the Image path through the
    /// existing absolute-path validation/commit path. A cancel leaves the path and config unchanged.
    /// </summary>
    private void BrowseImage()
    {
        var picker = _imageFilePicker;
        if (picker is null)
        {
            return;
        }

        var picked = picker.PickImage();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            ImagePathText = picked;
        }
    }

    private double ReadMinimum() => _config switch
    {
        GaugeWidgetConfig gauge => gauge.Minimum,
        SliderWidgetConfig slider => slider.Minimum,
        BarWidgetConfig bar => bar.Minimum,
        _ => 0d,
    };

    private double ReadMaximum() => _config switch
    {
        GaugeWidgetConfig gauge => gauge.Maximum,
        SliderWidgetConfig slider => slider.Maximum,
        BarWidgetConfig bar => bar.Maximum,
        _ => 0d,
    };

    private double ReadStep() => _config is SliderWidgetConfig slider ? slider.Step : 0d;

    private double ReadValue() => _config is SliderWidgetConfig slider ? slider.Value : 0d;

    private double ReadWindow() => _config is SparklineWidgetConfig sparkline
        ? sparkline.WindowSeconds
        : SparklineWidgetConfig.DefaultWindowSeconds;

    /// <summary>
    /// Sets the inline error for a numeric field. The volume level lives on the action picker, so
    /// its feedback goes to the picker's own validation line instead of this editor's.
    /// </summary>
    /// <param name="field">The field key.</param>
    /// <param name="message">The message.</param>
    private void SetNumericFieldError(string field, string message)
    {
        if (string.Equals(field, nameof(ActionPickerViewModel.LevelText), StringComparison.Ordinal))
        {
            ActionPicker?.SetError(message);
            return;
        }

        SetError(message);
    }

    /// <summary>Clears the inline error for a numeric field (see <see cref="SetNumericFieldError"/>).</summary>
    /// <param name="field">The field key.</param>
    private void ClearNumericFieldError(string field)
    {
        if (string.Equals(field, nameof(ActionPickerViewModel.LevelText), StringComparison.Ordinal))
        {
            ActionPicker?.SetError(null);
            return;
        }

        ClearError();
    }

    private void SetError(string message)
    {
        if (string.Equals(_validationError, message, StringComparison.Ordinal))
        {
            return;
        }

        _validationError = message;
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(HasValidationError));
    }

    private void ClearError()
    {
        if (_validationError is null)
        {
            return;
        }

        _validationError = null;
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(HasValidationError));
    }

    private bool SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);

        // The stepper enablement is derived from the field's text, so keep it in sync for every
        // write path (typing, commit normalization, a step, the empty/invalid branches).
        if (string.Equals(propertyName, nameof(FontSizeText), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(CanDecreaseFontSize));
            OnPropertyChanged(nameof(CanIncreaseFontSize));
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static bool TryParseTarget(string? value, PropertyPickerKind kind, out PropertyPickerTarget target)
    {
        target = PropertyPickerTarget.None;
        if (!Enum.TryParse<PropertyPickerTarget>(value, ignoreCase: true, out var parsed)
            || parsed == PropertyPickerTarget.None)
        {
            return false;
        }

        var isIcon = parsed is PropertyPickerTarget.Icon
            or PropertyPickerTarget.LabelIcon
            or PropertyPickerTarget.OnIcon;
        if ((kind == PropertyPickerKind.Icon) != isIcon)
        {
            return false;
        }

        target = parsed;
        return true;
    }

    private static bool IsFiniteMagnitude(double value) =>
        double.IsFinite(value) && Math.Abs(value) <= ConfigValidator.MaxGaugeMagnitude;

    private static bool IsAbsolutePath(string path)
    {
        try
        {
            return Path.IsPathRooted(path);
        }
        catch (Exception)
        {
            // Hostile input must never throw from the editor; treat it as not absolute.
            return false;
        }
    }

    private static bool TryParseDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
