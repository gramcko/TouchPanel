using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchPanel.Config;

/// <summary>Action <c>type</c> discriminator values for the M1 catalog (RF-048).</summary>
public static class ActionTypes
{
    /// <summary>Run command / launch application with an explicit executable and discrete arguments.</summary>
    public const string RunCommand = "runCommand";

    /// <summary>Media key (play/pause, next, previous, stop).</summary>
    public const string MediaKey = "mediaKey";

    /// <summary>Volume control (set absolute level or step up/down).</summary>
    public const string Volume = "volume";

    /// <summary>Mute toggle.</summary>
    public const string Mute = "mute";

    /// <summary>Send a single key + modifier combination to the foreground application (RF-057, schema v2).</summary>
    public const string SendKeys = "sendKeys";

    /// <summary>
    /// Reference a configured global hotkey and run its bound action (M5-RF-520, additive
    /// discriminator; no schema bump). The referenced hotkey is an entry in
    /// <c>PanelConfig.Hotkeys</c> identified by <see cref="HotkeyConfig.Id"/>.
    /// </summary>
    public const string Hotkey = "hotkey";
}

/// <summary>
/// Base configuration for a widget-bound action (RF-047, RF-048). Actions are typed with explicit
/// parameters and are validated at load; this model only carries the raw values.
/// </summary>
public abstract class ActionConfig
{
    /// <summary>
    /// Gets the <c>type</c> discriminator (DD-9). Serialized as <c>type</c>; the concrete type is
    /// selected on read by <c>ActionConfigJsonConverter</c>.
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// Run command / launch application (RF-048, RF-049): an explicit executable path, a discrete
/// argument list, and an optional working directory. The model deliberately has no shell/command
/// string field (D-7).
/// </summary>
public sealed class RunCommandActionConfig : ActionConfig
{
    private string _path = string.Empty;
    private List<string> _arguments = [];

    /// <inheritdoc/>
    public override string Type => ActionTypes.RunCommand;

    /// <summary>Gets or sets the explicit executable path. Never <see langword="null"/>; empty when unconfigured.</summary>
    public string Path
    {
        get => _path;
        set => _path = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the discrete argument list passed to the executable as separate values, never
    /// as a shell-parsed command line (RF-049). Never <see langword="null"/>; empty when omitted.
    /// </summary>
    public List<string> Arguments
    {
        get => _arguments;
        set => _arguments = value ?? [];
    }

    /// <summary>Gets or sets the optional working directory for the launched process.</summary>
    public string? WorkingDirectory { get; set; }
}

/// <summary>Media keys supported in M1 (RF-048).</summary>
public enum MediaKeyKind
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation disables the
    /// widget with a warning (RF-047); it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>Play/pause toggle.</summary>
    PlayPause,

    /// <summary>Next track.</summary>
    Next,

    /// <summary>Previous track.</summary>
    Previous,

    /// <summary>Stop playback.</summary>
    Stop,
}

/// <summary>Media key action (RF-048).</summary>
public sealed class MediaKeyActionConfig : ActionConfig
{
    /// <inheritdoc/>
    public override string Type => ActionTypes.MediaKey;

    /// <summary>Gets or sets the media key. Defaults to <see cref="MediaKeyKind.PlayPause"/>.</summary>
    public MediaKeyKind Key { get; set; } = MediaKeyKind.PlayPause;
}

/// <summary>How a volume action behaves (RF-048).</summary>
public enum VolumeMode
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation disables the
    /// widget with a warning (RF-047); it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>Set the absolute volume level (<see cref="VolumeActionConfig.Level"/>).</summary>
    Set,

    /// <summary>Step the volume up or down (<see cref="VolumeActionConfig.Direction"/>).</summary>
    Step,
}

/// <summary>Volume step direction (RF-048).</summary>
public enum VolumeDirection
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation disables the
    /// widget with a warning (RF-047); it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>Increase the volume.</summary>
    Up,

    /// <summary>Decrease the volume.</summary>
    Down,
}

/// <summary>
/// Volume action (RF-048): either sets an absolute level (<see cref="VolumeMode.Set"/>) or steps
/// the volume up/down (<see cref="VolumeMode.Step"/>). The field that does not apply to the chosen
/// mode is ignored by the handler after validation has clamped the applicable one.
/// </summary>
public sealed class VolumeActionConfig : ActionConfig
{
    /// <summary>Sensible default level applied when <c>level</c> is omitted in set mode (never 0/mute).</summary>
    public const int DefaultLevel = 50;

    /// <inheritdoc/>
    public override string Type => ActionTypes.Volume;

    /// <summary>Gets or sets the volume mode. Defaults to <see cref="VolumeMode.Set"/>.</summary>
    public VolumeMode Mode { get; set; } = VolumeMode.Set;

    /// <summary>Gets or sets the absolute volume level (0-100) used when <see cref="Mode"/> is <see cref="VolumeMode.Set"/>.</summary>
    public int Level { get; set; } = DefaultLevel;

    /// <summary>Gets or sets the step direction used when <see cref="Mode"/> is <see cref="VolumeMode.Step"/>.</summary>
    public VolumeDirection Direction { get; set; } = VolumeDirection.Up;
}

/// <summary>Mute toggle action (RF-048).</summary>
public sealed class MuteActionConfig : ActionConfig
{
    /// <inheritdoc/>
    public override string Type => ActionTypes.Mute;
}

/// <summary>
/// Send-keys action (RF-057, schema v2/M2-RF-571): one simultaneous key + modifier combination
/// delivered to the foreground application. The friendly-name→virtual-key mapping and the
/// known-modifier validation live in the action layer (T-02); this model only carries the raw
/// values, with presence enforced by <see cref="Validation.ConfigValidator"/>.
/// </summary>
public sealed class SendKeysActionConfig : ActionConfig
{
    private string _key = string.Empty;
    private List<string> _modifiers = [];

    /// <inheritdoc/>
    public override string Type => ActionTypes.SendKeys;

    /// <summary>Gets or sets the friendly key name (for example <c>A</c> or <c>F5</c>); never <see langword="null"/>, empty when unconfigured.</summary>
    public string Key
    {
        get => _key;
        set => _key = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the modifier names (<c>Ctrl</c>/<c>Alt</c>/<c>Shift</c>/<c>Win</c>). Never
    /// <see langword="null"/>; empty when omitted (a bare key is valid for a widget action). The set
    /// is validated against the fixed mapping in the action layer (T-02).
    /// </summary>
    public List<string> Modifiers
    {
        get => _modifiers;
        set => _modifiers = value ?? [];
    }
}

/// <summary>
/// Hotkey-reference action (M5-RF-520): carries only the referenced hotkey's id and nothing else.
/// The hotkey's key/combination and its bound action stay the single source of truth in
/// <see cref="HotkeyConfig"/>; execution resolves the referenced action at run time (M5-RF-521).
/// The model is plain data (a string id) so it round-trips under schema v4 (M5-RF-524).
/// </summary>
public sealed class RunHotkeyActionConfig : ActionConfig
{
    private string _hotkeyId = string.Empty;

    /// <inheritdoc/>
    public override string Type => ActionTypes.Hotkey;

    /// <summary>
    /// Gets or sets the referenced <see cref="HotkeyConfig.Id"/>; never <see langword="null"/>, empty
    /// when unconfigured. A blank or missing reference disables the widget with a reason (M5-RF-523).
    /// </summary>
    public string HotkeyId
    {
        get => _hotkeyId;
        set => _hotkeyId = value ?? string.Empty;
    }
}

/// <summary>
/// Preserves an action entry whose <c>type</c> is not part of this build (the DD-9 unknown-type
/// seam). A widget carrying one stays visible but is disabled with an error indicator (RF-047,
/// RF-079); the original JSON is kept so no information is lost while the type is unsupported.
/// </summary>
public sealed class UnknownActionConfig : ActionConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownActionConfig"/> class.
    /// </summary>
    /// <param name="type">The original <c>type</c> discriminator value.</param>
    /// <param name="raw">The complete original JSON object, cloned so it outlives its document.</param>
    public UnknownActionConfig(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type;
        Raw = raw;
    }

    /// <inheritdoc/>
    public override string Type { get; }

    /// <summary>Gets the complete original JSON object for the unknown entry.</summary>
    [JsonIgnore]
    public JsonElement Raw { get; }
}
