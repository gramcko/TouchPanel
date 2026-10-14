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
        base.ApplyConfig(config);

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
        OnPropertyChanged(nameof(IsOn));
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
        OnPropertyChanged(nameof(IsOn));

        if (_persistState && !string.IsNullOrEmpty(_pageId))
        {
            _stateChanged?.Invoke(_pageId, WidgetId, _isOn);
        }

        return ExecuteBoundActionAsync(cancellationToken);
    }
}
