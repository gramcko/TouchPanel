using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchPanel.Config;

/// <summary>Widget <c>type</c> discriminator values for the M1 catalog (RF-033).</summary>
public static class WidgetTypes
{
    /// <summary>The Button widget type.</summary>
    public const string Button = "button";

    /// <summary>The Toggle widget type.</summary>
    public const string Toggle = "toggle";

    /// <summary>The Label widget type.</summary>
    public const string Label = "label";

    /// <summary>The Gauge widget type.</summary>
    public const string Gauge = "gauge";

    /// <summary>The Slider widget type (RF-040, schema v2).</summary>
    public const string Slider = "slider";

    /// <summary>The Sparkline/graph widget type (RF-041, schema v2).</summary>
    public const string Sparkline = "sparkline";

    /// <summary>The Clock widget type (RF-046/M3-RF-462, schema v3).</summary>
    public const string Clock = "clock";

    /// <summary>The ImageButton widget type (RF-046/M3-RF-464, schema v3).</summary>
    public const string ImageButton = "imageButton";

    /// <summary>The Bar widget type (RF-046/M3-RF-466, schema v3).</summary>
    public const string Bar = "bar";

    /// <summary>The Image widget type (user-requested PNG/raster display, additive discriminator).</summary>
    public const string Image = "image";
}

/// <summary>
/// Base configuration for one positioned widget (RF-032, RF-034). Geometry is expressed in DIPs;
/// clamping and minimum sizes are validation concerns (RF-079) so the raw values are preserved here.
/// </summary>
public abstract class WidgetConfig
{
    private string _id = string.Empty;

    /// <summary>Gets or sets the widget identifier, unique within its page and used in action logs.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>
    /// Gets the <c>type</c> discriminator (DD-9). Serialized as <c>type</c>; the concrete type is
    /// selected on read by <c>WidgetConfigJsonConverter</c>.
    /// </summary>
    public abstract string Type { get; }

    /// <summary>Gets or sets the horizontal position in DIPs.</summary>
    public double X { get; set; }

    /// <summary>Gets or sets the vertical position in DIPs.</summary>
    public double Y { get; set; }

    /// <summary>Gets or sets the width in DIPs.</summary>
    public double Width { get; set; }

    /// <summary>Gets or sets the height in DIPs.</summary>
    public double Height { get; set; }

    /// <summary>Gets or sets the display text (button/toggle/label caption or gauge title).</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the id of the built-in icon shown by the widget, or <see langword="null"/> for
    /// no icon (RF-043/M2-RF-435). The value must be one of <see cref="IconCatalog.BuiltIn"/>;
    /// validation clears an unknown id with a warning. External icon packs are out of scope (M3).
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the foreground color override as <c>#RRGGBB</c> or <c>#AARRGGBB</c>, or
    /// <see langword="null"/> to use the theme default (RF-043/M2-RF-436). Validation clears an
    /// invalid value with a warning.
    /// </summary>
    public string? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the background color override as <c>#RRGGBB</c> or <c>#AARRGGBB</c>, or
    /// <see langword="null"/> to use the theme default (RF-043/M2-RF-436).
    /// </summary>
    public string? Background { get; set; }

    /// <summary>
    /// Gets or sets the accent color override as <c>#RRGGBB</c> or <c>#AARRGGBB</c>, or
    /// <see langword="null"/> to use the theme accent (RF-043/M2-RF-436).
    /// </summary>
    public string? Accent { get; set; }

    /// <summary>
    /// Gets or sets the font-size override in DIPs, or <see langword="null"/> to use the theme
    /// default (RF-043/M2-RF-436). Validation clamps the value into
    /// <see cref="WidgetAppearance.MinFontSize"/>..<see cref="WidgetAppearance.MaxFontSize"/>.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the opacity override in <c>[0.0, 1.0]</c>, or <see langword="null"/> to use the
    /// theme default (RF-043/M2-RF-436). Validation clamps the value into
    /// <see cref="WidgetAppearance.MinOpacity"/>..<see cref="WidgetAppearance.MaxOpacity"/>.
    /// </summary>
    public double? Opacity { get; set; }

    /// <summary>
    /// Gets or sets the bound action (RF-047). Required for Button/Toggle; <see langword="null"/>
    /// for widgets without an action. Unknown action types are preserved as
    /// <see cref="UnknownActionConfig"/> so validation can disable the widget.
    /// </summary>
    public ActionConfig? Action { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether validation allows this widget to run (RF-047,
    /// RF-079). A widget that is disabled stays in the configuration and is rendered with an
    /// error indicator instead of being dropped. This is validation output, not configuration,
    /// so it is never serialized.
    /// </summary>
    [JsonIgnore]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the human-readable reason a widget was disabled, shown by the error indicator
    /// and written to logs. <see langword="null"/> while the widget is enabled.
    /// </summary>
    [JsonIgnore]
    public string? DisabledReason { get; set; }
}

/// <summary>A Button widget (RF-033): executes its action once per completed tap.</summary>
public sealed class ButtonWidgetConfig : WidgetConfig
{
    /// <inheritdoc/>
    public override string Type => WidgetTypes.Button;
}

/// <summary>A Toggle widget (RF-033, D-15): flips runtime state and executes its action once per flip.</summary>
public sealed class ToggleWidgetConfig : WidgetConfig
{
    /// <inheritdoc/>
    public override string Type => WidgetTypes.Toggle;

    /// <summary>
    /// Gets or sets the on/off state applied when the page loads. Runtime flips are not persisted
    /// in M1 (D-15). Defaults to <see langword="false"/>.
    /// </summary>
    public bool InitialOn { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the runtime on/off state survives a restart
    /// (RF-045/M2-RF-451). Defaults to <see langword="false"/>, preserving D-15; when
    /// <see langword="false"/> the toggle resets to <see cref="InitialOn"/>. Persisted state lives
    /// in <c>state.json</c>, never in <c>panel.json</c>.
    /// </summary>
    public bool PersistState { get; set; }
}

/// <summary>A Label widget (RF-033): displays the configured <see cref="WidgetConfig.Label"/> text.</summary>
public sealed class LabelWidgetConfig : WidgetConfig
{
    /// <inheritdoc/>
    public override string Type => WidgetTypes.Label;
}

/// <summary>
/// Per-widget byte/data-rate display unit (user-requested addition): the unit a byte- or
/// rate-valued sensor is formatted in. <see cref="Auto"/> preserves the existing human-friendly
/// formatting; a fixed unit formats the value in that binary unit (rates keep the <c>/s</c>
/// suffix). The option is ignored for non-byte sensors (percent/temperature/RPM).
/// </summary>
public enum DisplayUnit
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation treats it as
    /// <see cref="Auto"/> with a warning; it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>Automatically choose the human-friendly unit (the existing behavior). Default.</summary>
    Auto,

    /// <summary>Format the value in bytes (<c>B</c>).</summary>
    Bytes,

    /// <summary>Format the value in kibibytes (<c>KB</c>).</summary>
    Kilobytes,

    /// <summary>Format the value in mebibytes (<c>MB</c>).</summary>
    Megabytes,

    /// <summary>Format the value in gibibytes (<c>GB</c>).</summary>
    Gigabytes,
}

/// <summary>A Gauge widget (RF-033, RF-034): renders a telemetry sensor over a display range.</summary>
public sealed class GaugeWidgetConfig : WidgetConfig
{
    /// <summary>Default lower bound of the display range (mirrors <see cref="BarWidgetConfig.DefaultMinimum"/>).</summary>
    public const double DefaultMinimum = 0;

    /// <summary>Default upper bound of the display range (mirrors <see cref="BarWidgetConfig.DefaultMaximum"/>).</summary>
    public const double DefaultMaximum = 100;

    /// <inheritdoc/>
    public override string Type => WidgetTypes.Gauge;

    /// <summary>
    /// Gets or sets the application-level sensor identifier to display (for example <c>cpu.total</c>),
    /// or <see langword="null"/> when unconfigured. Sensor ids are resolved by the monitoring layer.
    /// </summary>
    public string? Sensor { get; set; }

    /// <summary>Gets or sets the lower bound of the display range. Defaults to <see cref="DefaultMinimum"/>.</summary>
    public double Minimum { get; set; } = DefaultMinimum;

    /// <summary>Gets or sets the upper bound of the display range. Defaults to <see cref="DefaultMaximum"/>.</summary>
    public double Maximum { get; set; } = DefaultMaximum;

    /// <summary>
    /// Gets or sets the display unit used when the bound sensor reports bytes or bytes per second
    /// (user-requested addition). <see cref="DisplayUnit.Auto"/> preserves the existing automatic
    /// formatting; a fixed unit formats the value in that unit (rates keep the <c>/s</c> suffix).
    /// The option is ignored for non-byte sensors. Older files without the field default to
    /// <see cref="DisplayUnit.Auto"/>.
    /// </summary>
    public DisplayUnit DisplayUnit { get; set; } = DisplayUnit.Auto;
}

/// <summary>Slider track orientation (RF-040/M2-RF-404).</summary>
public enum SliderOrientation
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation treats it as
    /// <see cref="Horizontal"/> with a warning; it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>Range maps left→right (minimum→maximum). Default.</summary>
    Horizontal,

    /// <summary>Range maps bottom→top (minimum at the bottom).</summary>
    Vertical,
}

/// <summary>
/// A Slider widget (RF-040, schema v2): a numeric range/step track that drives a numeric action
/// parameter. Validation normalizes invalid ranges/steps (M2-RF-401); the value is clamped and
/// snapped to the step.
/// </summary>
public sealed class SliderWidgetConfig : WidgetConfig
{
    /// <inheritdoc/>
    public override string Type => WidgetTypes.Slider;

    /// <summary>Gets or sets the lower bound of the slider range. Defaults to <c>0</c>.</summary>
    public double Minimum { get; set; }

    /// <summary>Gets or sets the upper bound of the slider range. Defaults to <c>100</c>.</summary>
    public double Maximum { get; set; } = 100;

    /// <summary>Gets or sets the step increment (must be &gt; 0 and ≤ the range). Defaults to <c>1</c>.</summary>
    public double Step { get; set; } = 1;

    /// <summary>Gets or sets the track orientation. Defaults to <see cref="SliderOrientation.Horizontal"/>.</summary>
    public SliderOrientation Orientation { get; set; } = SliderOrientation.Horizontal;

    /// <summary>Gets or sets the initial value; clamped into <c>[minimum, maximum]</c> and rounded to the step by validation.</summary>
    public double Value { get; set; }
}

/// <summary>Sparkline render style (RF-041/M2-RF-411).</summary>
public enum SparklineStyle
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation treats it as
    /// <see cref="Line"/> with a warning; it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>A line over the history window. Default.</summary>
    Line,

    /// <summary>A line with a filled area beneath it.</summary>
    Filled,
}

/// <summary>
/// A Sparkline/graph widget (RF-041, schema v2): renders the monitoring layer's bounded history for
/// a sensor key. Validation clamps the window and normalizes the style (M2-RF-411); unknown sensor
/// keys disable the widget (RF-047).
/// </summary>
public sealed class SparklineWidgetConfig : WidgetConfig
{
    /// <summary>Smallest accepted history window, in seconds (M2-RF-411).</summary>
    public const double MinWindowSeconds = 10;

    /// <summary>Largest accepted history window, in seconds (M2-RF-411).</summary>
    public const double MaxWindowSeconds = 3600;

    /// <summary>Default history window, in seconds (M2-RF-411).</summary>
    public const double DefaultWindowSeconds = 60;

    /// <inheritdoc/>
    public override string Type => WidgetTypes.Sparkline;

    /// <summary>Gets or sets the application-level sensor identifier to graph, or <see langword="null"/> when unconfigured.</summary>
    public string? Sensor { get; set; }

    /// <summary>Gets or sets the history window in seconds; defaults to <see cref="DefaultWindowSeconds"/> (60 s).</summary>
    public double WindowSeconds { get; set; } = DefaultWindowSeconds;

    /// <summary>Gets or sets the render style. Defaults to <see cref="SparklineStyle.Line"/>.</summary>
    public SparklineStyle Style { get; set; } = SparklineStyle.Line;

    /// <summary>Gets or sets the explicit lower bound, or <see langword="null"/> to auto-scale from the data.</summary>
    public double? Minimum { get; set; }

    /// <summary>Gets or sets the explicit upper bound, or <see langword="null"/> to auto-scale from the data.</summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current (latest) value is shown as text in the
    /// widget header (user-requested addition). Defaults to <see langword="false"/>, preserving the
    /// graph-only behavior; older files without the field load unchanged.
    /// </summary>
    public bool ShowValue { get; set; }

    /// <summary>
    /// Gets or sets the display unit used when the graphed sensor reports bytes or bytes per second
    /// (user-requested addition). <see cref="DisplayUnit.Auto"/> preserves the existing automatic
    /// formatting; a fixed unit formats the value in that unit (rates keep the <c>/s</c> suffix).
    /// The option is ignored for non-byte sensors. Older files without the field default to
    /// <see cref="DisplayUnit.Auto"/>.
    /// </summary>
    public DisplayUnit DisplayUnit { get; set; } = DisplayUnit.Auto;
}

/// <summary>Clock display preset (RF-046/M3-RF-462).</summary>
public enum ClockMode
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation treats it as
    /// <see cref="Time24"/> with a warning; it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>24-hour time (<c>HH:mm</c>). Default.</summary>
    Time24,

    /// <summary>12-hour time with the AM/PM designator (<c>h:mm tt</c>).</summary>
    Time12,

    /// <summary>Short date (culture short-date pattern).</summary>
    DateShort,

    /// <summary>Long date (culture long-date pattern).</summary>
    DateLong,

    /// <summary>General date and time (culture general pattern).</summary>
    DateTime,
}

/// <summary>
/// A Clock widget (RF-046/M3-RF-462/463, schema v3): display-only local time/date driven by a
/// preset <see cref="Mode"/> or an optional bounded custom <see cref="Format"/>. The widget has no
/// action binding.
/// </summary>
public sealed class ClockWidgetConfig : WidgetConfig
{
    /// <summary>Smallest accepted custom format length, in characters (M3-RF-462).</summary>
    public const int MinFormatLength = 1;

    /// <summary>Largest accepted custom format length, in characters (M3-RF-462).</summary>
    public const int MaxFormatLength = 64;

    /// <inheritdoc/>
    public override string Type => WidgetTypes.Clock;

    /// <summary>Gets or sets the display preset used when <see cref="Format"/> is absent or invalid.</summary>
    public ClockMode Mode { get; set; } = ClockMode.Time24;

    /// <summary>
    /// Gets or sets an optional custom date/time format string (length
    /// <see cref="MinFormatLength"/>..<see cref="MaxFormatLength"/>), or <see langword="null"/> to
    /// use the <see cref="Mode"/> preset. An invalid or over-length value is ignored with a warning
    /// and the preset is used (M3-RF-462).
    /// </summary>
    public string? Format { get; set; }
}

/// <summary>
/// An ImageButton widget (RF-046/M3-RF-464/465, schema v3): a touch-first icon button that renders
/// the configured built-in <see cref="WidgetConfig.Icon"/> (resolved through the active icon pack)
/// and executes exactly one bound action per completed tap. Arbitrary external image paths are out
/// of scope (RF-081).
/// </summary>
public sealed class ImageButtonWidgetConfig : WidgetConfig
{
    /// <inheritdoc/>
    public override string Type => WidgetTypes.ImageButton;

    /// <summary>
    /// Gets or sets a value indicating whether the configured <see cref="WidgetConfig.Label"/> is
    /// shown beneath the icon. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ShowLabel { get; set; } = true;
}

/// <summary>
/// A Bar widget (RF-046/M3-RF-466/467, schema v3): a single-value horizontal meter over the
/// public monitoring snapshot boundary, with a display range and an optional label/value.
/// </summary>
public sealed class BarWidgetConfig : WidgetConfig
{
    /// <summary>Default lower bound of the display range.</summary>
    public const double DefaultMinimum = 0;

    /// <summary>Default upper bound of the display range.</summary>
    public const double DefaultMaximum = 100;

    /// <inheritdoc/>
    public override string Type => WidgetTypes.Bar;

    /// <summary>
    /// Gets or sets the application-level sensor identifier to display (for example <c>cpu.total</c>),
    /// or <see langword="null"/> when unconfigured. Sensor ids are resolved by the monitoring layer.
    /// </summary>
    public string? Sensor { get; set; }

    /// <summary>Gets or sets the lower bound of the display range. Defaults to <see cref="DefaultMinimum"/>.</summary>
    public double Minimum { get; set; } = DefaultMinimum;

    /// <summary>Gets or sets the upper bound of the display range. Defaults to <see cref="DefaultMaximum"/>.</summary>
    public double Maximum { get; set; } = DefaultMaximum;

    /// <summary>Gets or sets a value indicating whether the numeric value is shown. Defaults to <see langword="true"/>.</summary>
    public bool ShowValue { get; set; } = true;

    /// <summary>
    /// Gets or sets the display unit used when the bound sensor reports bytes or bytes per second
    /// (user-requested addition). <see cref="DisplayUnit.Auto"/> preserves the existing automatic
    /// formatting; a fixed unit formats the value in that unit (rates keep the <c>/s</c> suffix).
    /// The option is ignored for non-byte sensors. Older files without the field default to
    /// <see cref="DisplayUnit.Auto"/>.
    /// </summary>
    public DisplayUnit DisplayUnit { get; set; } = DisplayUnit.Auto;
}

/// <summary>Image fit mode for the <see cref="ImageWidgetConfig"/> (WPF <c>Stretch</c> values).</summary>
public enum ImageStretchMode
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation treats it as
    /// <see cref="Uniform"/> with a warning; it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>Scale the image uniformly to fit the bounds, preserving aspect ratio. Default.</summary>
    Uniform,

    /// <summary>Stretch the image to fill the bounds, ignoring aspect ratio.</summary>
    Fill,

    /// <summary>Scale the image uniformly so it fills the bounds, cropping the overflow.</summary>
    UniformToFill,

    /// <summary>Center the image at its native size without scaling.</summary>
    None,
}

/// <summary>
/// An Image widget (user-requested addition, additive discriminator): displays a raster image
/// (PNG and the other formats <see cref="System.Windows.Media.Imaging.BitmapImage"/> can decode)
/// loaded from a configured absolute file path, scaled into its bounds by the existing edit-mode
/// resize. The path is untrusted configuration: only the file is read and decoded, never executed;
/// the decode is bounded so a large file cannot exhaust memory. An empty, non-absolute, missing, or
/// undecodable path shows an explicit placeholder instead of crashing.
/// </summary>
public sealed class ImageWidgetConfig : WidgetConfig
{
    /// <inheritdoc/>
    public override string Type => WidgetTypes.Image;

    /// <summary>
    /// Gets or sets the absolute path of the image file to display, or <see langword="null"/> when
    /// unconfigured. Validation requires a non-empty absolute path; a relative path is rejected
    /// (never resolved against the current directory) so an untrusted value cannot select an
    /// unintended file. The file is only read/decoded, never executed.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the fit mode. Defaults to <see cref="ImageStretchMode.Uniform"/>.</summary>
    public ImageStretchMode Stretch { get; set; } = ImageStretchMode.Uniform;
}

/// <summary>
/// Preserves a widget entry whose <c>type</c> is not part of this build (the DD-9 unknown-type
/// seam). Validation skips these entries (RF-079), and the original JSON is kept so no information
/// is lost while the type is unsupported.
/// </summary>
public sealed class UnknownWidgetConfig : WidgetConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownWidgetConfig"/> class.
    /// </summary>
    /// <param name="type">The original <c>type</c> discriminator value.</param>
    /// <param name="raw">The complete original JSON object, cloned so it outlives its document.</param>
    public UnknownWidgetConfig(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type;
        Raw = raw;

        if (raw.ValueKind == JsonValueKind.Object
            && raw.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String)
        {
            Id = id.GetString() ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public override string Type { get; }

    /// <summary>Gets the complete original JSON object for the unknown entry.</summary>
    [JsonIgnore]
    public JsonElement Raw { get; }
}
