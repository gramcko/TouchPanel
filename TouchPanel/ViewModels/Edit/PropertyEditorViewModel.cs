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
public sealed class PropertyEditorViewModel : INotifyPropertyChanged
{
    /// <summary>Maximum accepted label length in characters (M2-RF-435).</summary>
    public const int MaxLabelLength = 128;

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
    private string _foregroundText = string.Empty;
    private string _backgroundText = string.Empty;
    private string _accentText = string.Empty;
    private string _fontSizeText = string.Empty;
    private string _opacityText = string.Empty;
    private string _formatText = string.Empty;
    private string _imagePathText = string.Empty;
    private bool _showLabel;
    private bool _showValue;
    private string? _validationError;

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
        IImageFilePicker? imageFilePicker = null)
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
        _selectedIcon = IconCatalog.TryGet(config.Icon, out var icon) ? icon : NoneIcon;
        _foregroundText = config.Foreground ?? _themeDefaults.Foreground;
        _backgroundText = config.Background ?? _themeDefaults.Background;
        _accentText = config.Accent ?? _themeDefaults.Accent;
        _fontSizeText = config.FontSize is double fontSize ? FormatNumber(fontSize) : FormatNumber(_themeDefaults.FontSize);
        _opacityText = config.Opacity is double opacity ? FormatNumber(opacity) : FormatNumber(_themeDefaults.Opacity);

        // M5-RF-531: an explicit, idempotent Apply command so the property panel's Apply affordance
        // performs a real commit instead of acting only as a focus target.
        ApplyCommand = new RelayCommand(Apply);

        // User-requested addition: byte/data-rate display unit (Gauge/Bar) and image-file browse.
        DisplayUnitOptions =
        [
            new(DisplayUnit.Auto, "Auto"),
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
    /// human-friendly existing behavior). The unknown sentinel is never offered.
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

            CommitConfig();
        }
    }

    /// <summary>Gets or sets the range minimum (gauge/slider); the value is validated against the span.</summary>
    public string MinimumText
    {
        get => _minimumText;
        set
        {
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

    private bool TrySetMinimum(double minimum)
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

        CommitConfig();
        return true;
    }

    private bool TrySetMaximum(double maximum)
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

        CommitConfig();
        return true;
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
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
