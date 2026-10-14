namespace TouchPanel.Config;

/// <summary>
/// Root of the <c>panel.json</c> schema (RF-074). All values are plain data: the model never
/// executes, resolves, or interprets configuration; that is the job of validation (entry level)
/// and the subsystems that consume the values.
/// </summary>
public sealed class PanelConfig
{
    /// <summary>The schema version this build understands; a newer version is left untouched (RF-075.4).</summary>
    public const int CurrentSchemaVersion = 4;

    private List<PageConfig> _pages = [];
    private List<HotkeyConfig> _hotkeys = [];
    private MonitorConfig _monitor = new();
    private WindowConfig _window = new();
    private StartupConfig _startup = new();
    private TelemetryConfig _telemetry = new();
    private AppearanceConfig _appearance = new();

    /// <summary>
    /// Gets or sets the explicit schema version near the root (RF-074). A missing property reads as
    /// <c>0</c>, which validation classifies as a file-level problem; the current version is
    /// <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets the id of the page to render (RF-037). When missing or not present in
    /// <see cref="Pages"/>, validation substitutes the first page and logs the substitution.
    /// </summary>
    public string? ActivePageId { get; set; }

    /// <summary>Gets or sets the target-monitor identity and fallback policy (RF-015..RF-017).</summary>
    public MonitorConfig Monitor
    {
        get => _monitor;
        set => _monitor = value ?? new MonitorConfig();
    }

    /// <summary>Gets or sets window behavior options (RF-020).</summary>
    public WindowConfig Window
    {
        get => _window;
        set => _window = value ?? new WindowConfig();
    }

    /// <summary>Gets or sets start-with-Windows options (RF-003).</summary>
    public StartupConfig Startup
    {
        get => _startup;
        set => _startup = value ?? new StartupConfig();
    }

    /// <summary>
    /// Gets or sets telemetry polling options (RF-071, schema v2). Defaults preserve the M1
    /// ~1 Hz cadence.
    /// </summary>
    public TelemetryConfig Telemetry
    {
        get => _telemetry;
        set => _telemetry = value ?? new TelemetryConfig();
    }

    /// <summary>
    /// Gets or sets the global appearance configuration (M3-RF-841, schema v4): the active built-in
    /// theme, an optional global accent color, and the active built-in icon pack. Never
    /// <see langword="null"/>; a missing or explicit-null section is normalized to a fresh default
    /// instance (theme <c>dark</c>, no accent, icon pack <c>default</c>).
    /// </summary>
    public AppearanceConfig Appearance
    {
        get => _appearance;
        set => _appearance = value ?? new AppearanceConfig();
    }

    /// <summary>
    /// Gets or sets the global hotkey bindings (RF-058, schema v2). Never <see langword="null"/>
    /// (an explicit JSON null is normalized). Registration and validation of the entries belong to
    /// the input subsystem (T-09); this model only carries the raw values.
    /// </summary>
    public List<HotkeyConfig> Hotkeys
    {
        get => _hotkeys;
        set => _hotkeys = value ?? [];
    }

    /// <summary>Gets or sets the pages; never <see langword="null"/> (an explicit JSON null is normalized).</summary>
    public List<PageConfig> Pages
    {
        get => _pages;
        set => _pages = value ?? [];
    }
}

/// <summary>
/// Target-monitor selection: a stable-identity match block (RF-016) plus the policy to apply when
/// the monitor is unavailable (RF-017/D-2).
/// </summary>
public sealed class MonitorConfig
{
    /// <summary>
    /// Gets or sets the identity fields the target monitor must match, or <see langword="null"/>
    /// when no criteria are configured (first run, D-17).
    /// </summary>
    public MonitorIdentity? Match { get; set; }

    /// <summary>
    /// Gets or sets the unavailable-monitor policy. Defaults to <see cref="MonitorFallback.None"/>
    /// (stay hidden; D-2); <see cref="MonitorFallback.Primary"/> is an explicit opt-in and is never
    /// applied silently.
    /// </summary>
    public MonitorFallback Fallback { get; set; } = MonitorFallback.None;
}

/// <summary>
/// Stable monitor identity fields in priority order (RF-016): EDID-derived hardware identity,
/// display device path / device instance identifier, then friendly name. Any field may be absent;
/// matching is performed by the monitor subsystem over discovered descriptors.
/// </summary>
public sealed class MonitorIdentity
{
    /// <summary>Gets or sets the EDID-derived hardware identity (manufacturer + product code + serial), when available.</summary>
    public string? HardwareId { get; set; }

    /// <summary>Gets or sets the display device path / device instance identifier.</summary>
    public string? DevicePath { get; set; }

    /// <summary>Gets or sets the friendly monitor name.</summary>
    public string? FriendlyName { get; set; }
}

/// <summary>Unavailable-target-monitor policy (RF-017/D-2).</summary>
public enum MonitorFallback
{
    /// <summary>
    /// Sentinel produced when the configured JSON value is not recognized. Validation treats it as
    /// <see cref="None"/> with a warning; it is never a valid configured value.
    /// </summary>
    Unknown = 0,

    /// <summary>Stay hidden, notify, and show automatically when the configured monitor returns (default).</summary>
    None,

    /// <summary>Fall back to the primary monitor, always accompanied by a notification.</summary>
    Primary,
}

/// <summary>Window behavior options (RF-020, D-3).</summary>
public sealed class WindowConfig
{
    /// <summary>Gets or sets a value indicating whether the panel stays above other windows; defaults to <see langword="false"/> (D-3).</summary>
    public bool AlwaysOnTop { get; set; }
}

/// <summary>Start-with-Windows options (RF-003, D-4).</summary>
public sealed class StartupConfig
{
    /// <summary>Gets or sets a value indicating whether the per-user startup registration should exist; defaults to <see langword="false"/>.</summary>
    public bool StartWithWindows { get; set; }
}

/// <summary>
/// Telemetry polling options (RF-071, schema v2): the interval at which hardware telemetry is
/// sampled. The value is validated/clamped into <see cref="MinPollIntervalSeconds"/>..
/// <see cref="MaxPollIntervalSeconds"/>; the model only carries the raw value.
/// </summary>
public sealed class TelemetryConfig
{
    /// <summary>Lowest accepted polling interval, in seconds (RF-071).</summary>
    public const double MinPollIntervalSeconds = 0.5;

    /// <summary>Highest accepted polling interval, in seconds (RF-071).</summary>
    public const double MaxPollIntervalSeconds = 5.0;

    /// <summary>Default polling interval, in seconds (RF-071); preserves the M1 ~1 Hz cadence.</summary>
    public const double DefaultPollIntervalSeconds = 1.0;

    /// <summary>
    /// Gets or sets the polling interval in seconds; defaults to
    /// <see cref="DefaultPollIntervalSeconds"/> (1 s).
    /// </summary>
    public double PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;
}

/// <summary>
/// One global hotkey binding (RF-058, D-M2-4, schema v2): a unique id, a non-empty modifier set,
/// a key, and exactly one bound action identical in shape to a widget's binding. At least one
/// modifier is required; that rule and the friendly-name/key mapping are enforced by the input
/// subsystem (T-09), so this model only carries the raw values.
/// </summary>
public sealed class HotkeyConfig
{
    private string _id = string.Empty;
    private List<string> _modifiers = [];

    /// <summary>Gets or sets the unique hotkey identifier.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the modifier names (<c>Ctrl</c>/<c>Alt</c>/<c>Shift</c>/<c>Win</c>). Never
    /// <see langword="null"/>; empty when omitted.
    /// </summary>
    public List<string> Modifiers
    {
        get => _modifiers;
        set => _modifiers = value ?? [];
    }

    /// <summary>Gets or sets the friendly key name (for example <c>A</c> or <c>F5</c>), or <see langword="null"/> when unconfigured.</summary>
    public string? Key { get; set; }

    /// <summary>Gets or sets the single bound action, or <see langword="null"/> when unconfigured.</summary>
    public ActionConfig? Action { get; set; }
}
