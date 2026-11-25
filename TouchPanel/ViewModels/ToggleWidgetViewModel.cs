using System.Windows.Media;
using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// Toggle widget view model (RF-033/RF-035, D-15): the initial on/off state comes from configuration
/// (RF-034), each completed tap flips the visual state exactly once and executes the bound action
/// once per flip.
/// </summary>
/// <remarks>
/// State is runtime-only by default (D-15): nothing here writes back to <c>panel.json</c>, and a
/// rebuilt view model starts again from <see cref="ToggleWidgetConfig.InitialOn"/>. When the config
/// opts in (<see cref="ToggleWidgetConfig.PersistState"/>, RF-045/M2-RF-451), the initial state is
/// read from the persisted map and each flip is recorded so it survives a restart; persisted state
/// lives only in <c>state.json</c>, never in <c>panel.json</c>.
/// </remarks>
public sealed class ToggleWidgetViewModel : WidgetViewModelBase
{
    private readonly string? _pageId;
    private readonly Func<string, string, bool?>? _stateLookup;
    private readonly Action<string, string, bool>? _stateChanged;
    private bool _persistState;
    private bool _initialOn;
    private bool _isOn;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToggleWidgetViewModel"/> class.
    /// </summary>
    /// <param name="config">The validated toggle config; its <see cref="ToggleWidgetConfig.InitialOn"/> seeds the state.</param>
    /// <param name="actionExecutor">The action boundary (RF-050).</param>
    /// <param name="timeProvider">Clock for the transient error window; defaults to the system clock.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for widget-level failures.</param>
    /// <param name="pageId">The owning page id for persisted state (RF-045); <see langword="null"/> disables persistence.</param>
    /// <param name="stateLookup">Persisted-state lookup <c>(pageId, widgetId) =&gt; bool?</c>; <see langword="null"/> means no persisted state.</param>
    /// <param name="stateChanged">Persisted-state write <c>(pageId, widgetId, isOn)</c>; <see langword="null"/> leaves flips runtime-only.</param>
    public ToggleWidgetViewModel(
        ToggleWidgetConfig config,
        IActionExecutor? actionExecutor = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null,
        string? pageId = null,
        Func<string, string, bool?>? stateLookup = null,
        Action<string, string, bool>? stateChanged = null)
        : base(config, WidgetTemplateKeys.Toggle, actionExecutor, timeProvider, dispatch, logger)
    {
        _persistState = config.PersistState;
        _initialOn = config.InitialOn;
        _pageId = pageId;
        _stateLookup = stateLookup;
        _stateChanged = stateChanged;

        // RF-045/M2-RF-451/454: only opt-in toggles consult the persisted map; a missing key falls
        // back to the configured initial state, and without a page/store the widget stays M1.
        var persisted = _persistState && !string.IsNullOrEmpty(pageId)
            ? stateLookup?.Invoke(pageId, config.Id)
            : null;

        _isOn = persisted ?? config.InitialOn;
    }

    /// <summary>Gets a value indicating whether the toggle is currently on.</summary>
    public bool IsOn => _isOn;

    /// <summary>
    /// Gets the glyph rendered by the toggle's icon presenter: the configured on-state glyph while
    /// the toggle is on and an on icon is set, otherwise the base icon glyph (user-requested
    /// addition). With no on icon configured this is always <see cref="WidgetViewModelBase.IconGlyph"/>,
    /// preserving the previous behaviour.
    /// </summary>
    public string? DisplayIconGlyph => UsesAccentIcon ? OnIconGlyph : IconGlyph;

    /// <summary>
    /// Gets the vector path rendered by the toggle's icon presenter: the on-state vector while the
    /// toggle is on and an on icon is set, otherwise the base icon vector.
    /// </summary>
    public string? DisplayIconPathData => UsesAccentIcon ? OnIconPathData : IconPathData;

    /// <summary>
    /// Gets a value indicating whether <see cref="DisplayIconPathData"/> is line art drawn with a
    /// stroked outline rather than a filled shape.
    /// </summary>
    public bool DisplayIconStroke => UsesAccentIcon ? OnIconStroke : IconStroke;

    /// <summary>Gets the stroke pen width in the 24-unit viewbox for the displayed line-art icon, or <c>0</c>.</summary>
    public double DisplayIconStrokeUnits => UsesAccentIcon ? OnIconStrokeUnits : IconStrokeUnits;

    /// <summary>Gets a value indicating whether the toggle has an icon (glyph or vector) to render in its current state.</summary>
    public bool HasDisplayIcon => DisplayIconGlyph is not null || DisplayIconPathData is not null;

    /// <summary>
    /// Gets a value indicating whether the on-state icon is currently rendered in the accent colour
    /// (user-requested addition): only while the toggle is on <em>and</em> an on icon is configured.
    /// </summary>
    public bool UsesAccentIcon => _isOn && HasOnIcon;

    /// <summary>
    /// Gets the brush the icon presenter layers over the theme fallback: the per-widget accent
    /// override while <see cref="UsesAccentIcon"/> is <see langword="true"/>, otherwise the
    /// per-widget foreground override. A <see langword="null"/> value means the template's
    /// <c>PanelAccentBrush</c>/<c>PanelForegroundBrush</c> fallback applies.
    /// </summary>
    public Brush? DisplayIconBrush => UsesAccentIcon ? AccentBrush : ForegroundBrush;

    /// <inheritdoc />
    /// <remarks>
    /// When the persisted-state opt-in or the initial state changes (M2-RF-433), the live on/off
    /// value is recomputed from the persisted map (opt-in) or the configured initial state, so the
    /// edit is reflected immediately without a rebuild. Other property edits leave the runtime
    /// state untouched.
    /// </remarks>
    public override void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var previousUsesAccent = UsesAccentIcon;
        var previousAccent = AccentBrush;
        var previousForeground = ForegroundBrush;

        base.ApplyConfig(config);

        // The display brush follows the accent colour only in the on state; raise it when the
        // underlying effective brush actually changed (a live accent/foreground edit).
        var brushChanged = previousUsesAccent == UsesAccentIcon
            ? !BrushEquals(
                previousUsesAccent ? previousAccent : previousForeground,
                UsesAccentIcon ? AccentBrush : ForegroundBrush)
            : true;
        if (brushChanged)
        {
            OnPropertyChanged(nameof(DisplayIconBrush));
        }

        if (config is not ToggleWidgetConfig toggle
            || (_persistState == toggle.PersistState && _initialOn == toggle.InitialOn))
        {
            return;
        }

        _persistState = toggle.PersistState;
        _initialOn = toggle.InitialOn;

        var persisted = _persistState && !string.IsNullOrEmpty(_pageId)
            ? _stateLookup?.Invoke(_pageId, toggle.Id)
            : null;

        var nextState = persisted ?? toggle.InitialOn;
        if (_isOn == nextState)
        {
            return;
        }

        _isOn = nextState;
        NotifyOnStateChanged();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The flip happens before the action runs, so the visual state changes exactly once per tap
    /// even when the action then fails (the failure is surfaced separately per RF-036). For an
    /// opt-in toggle the new state is recorded through the persistence seam, which debounces the
    /// <c>state.json</c> write and never touches <c>panel.json</c> (M2-RF-455).
    /// </remarks>
    protected override Task ActivateCoreAsync(CancellationToken cancellationToken)
    {
        _isOn = !_isOn;
        NotifyOnStateChanged();

        if (_persistState && !string.IsNullOrEmpty(_pageId))
        {
            _stateChanged?.Invoke(_pageId, WidgetId, _isOn);
        }

        return ExecuteBoundActionAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override string? GetConfiguredOnIconId(WidgetConfig config) =>
        config is ToggleWidgetConfig toggle ? toggle.OnIcon : null;

    /// <inheritdoc />
    protected override void OnIconGlyphChanged() => NotifyDisplayIconChanged();

    /// <inheritdoc />
    protected override void OnOnIconGlyphChanged()
    {
        NotifyDisplayIconChanged();
        OnPropertyChanged(nameof(UsesAccentIcon));
        OnPropertyChanged(nameof(DisplayIconBrush));
    }

    /// <summary>
    /// Raises the on/off state change and, when an on-state icon is configured, the projected
    /// display-icon properties so the template swaps icon and accent without a rebuild. Without an
    /// on icon the display icon is identical in both states, so nothing extra is raised.
    /// </summary>
    private void NotifyOnStateChanged()
    {
        OnPropertyChanged(nameof(IsOn));

        if (!HasOnIcon)
        {
            return;
        }

        NotifyDisplayIconChanged();
        OnPropertyChanged(nameof(UsesAccentIcon));
        OnPropertyChanged(nameof(DisplayIconBrush));
    }

    /// <summary>Raises the display-icon properties (glyph, path, stroke style, and presence).</summary>
    private void NotifyDisplayIconChanged()
    {
        OnPropertyChanged(nameof(DisplayIconGlyph));
        OnPropertyChanged(nameof(DisplayIconPathData));
        OnPropertyChanged(nameof(DisplayIconStroke));
        OnPropertyChanged(nameof(DisplayIconStrokeUnits));
        OnPropertyChanged(nameof(HasDisplayIcon));
    }

    private static bool BrushEquals(Brush? first, Brush? second) =>
        ReferenceEquals(first, second)
        || (first is SolidColorBrush firstSolid
            && second is SolidColorBrush secondSolid
            && firstSolid.Color == secondSolid.Color);
}
