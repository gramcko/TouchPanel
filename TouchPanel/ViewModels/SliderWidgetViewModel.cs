using System.Globalization;
using System.Windows.Input;
using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace TouchPanel.ViewModels;

/// <summary>
/// Slider widget view model (T-06, RF-040/M2-RF-401..406): a config-driven numeric range/step track
/// whose current value is mapped onto the bound action's numeric parameter. For M2 the only
/// supported binding is the <c>volume</c> action in <see cref="VolumeMode.Set"/> mode, whose
/// <c>level</c> is replaced by the slider value normalized from the slider range onto <c>0–100</c>
/// (M2-RF-403). Any other bound action type loads the slider disabled with a visible indicator and
/// a log entry (RF-047) instead of silently ignoring the value.
/// </summary>
/// <remarks>
/// <para><b>Interaction and throttling (M2-RF-402).</b> Dragging updates <see cref="Value"/>
/// continuously through the template's two-way binding. While dragging, an action is emitted at
/// most once per <see cref="EmissionThrottle"/> <i>and</i> only after the snapped value changed
/// (the less frequent of the two gates). Finishing the gesture invokes
/// <see cref="CompleteInteractionAsync"/> through <see cref="CompleteInteractionCommand"/>, which
/// emits the final value exactly once and never duplicates the last emitted value.</para>
/// <para><b>Threading (RNF-004).</b> Values arrive on the UI thread from the control; the action
/// boundary is awaited asynchronously and failures surface as the shared ~2 s transient error
/// state (RF-036/RF-050).</para>
/// </remarks>
public sealed class SliderWidgetViewModel : WidgetViewModelBase
{
    /// <summary>
    /// Minimum interval between two actions emitted while the handle is still being dragged
    /// (M2-RF-402, default 100 ms).
    /// </summary>
    public static readonly TimeSpan EmissionThrottle = TimeSpan.FromMilliseconds(100);

    private readonly ILogger _logger;
    private readonly TimeProvider _clock;
    private double _minimum;
    private double _maximum;
    private double _step;
    private WpfOrientation _orientation;
    private bool _supportsValueBinding;
    private double _configuredValue;
    private double _value;
    private double? _lastEmittedValue;
    private DateTimeOffset _lastEmitAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="SliderWidgetViewModel"/> class from a validated
    /// slider config (M2-RF-401). Range/step/orientation/value are sanitized defensively so a
    /// programmatic caller that bypassed validation cannot produce non-finite UI math.
    /// </summary>
    /// <param name="config">The validated slider config; must not be <see langword="null"/>.</param>
    /// <param name="actionExecutor">The action boundary (RF-050); <see langword="null"/> leaves actions inert.</param>
    /// <param name="timeProvider">Clock for the emission throttle; defaults to the system clock.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for the unsupported-binding notice.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public SliderWidgetViewModel(
        SliderWidgetConfig config,
        IActionExecutor? actionExecutor = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.Slider, actionExecutor, timeProvider, dispatch, logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        (_minimum, _maximum, _step) = Sanitize(config.Minimum, config.Maximum, config.Step);
        _orientation = config.Orientation == SliderOrientation.Vertical
            ? WpfOrientation.Vertical
            : WpfOrientation.Horizontal;
        _configuredValue = config.Value;
        _value = Snap(config.Value);
        _logger = logger ?? Log.Logger;
        _clock = timeProvider ?? TimeProvider.System;

        _supportsValueBinding = EvaluateBindingSupport();

        CompleteInteractionCommand = new AsyncRelayCommand(() => CompleteInteractionAsync());
    }

    /// <summary>Gets the lower bound of the slider range.</summary>
    public double Minimum => _minimum;

    /// <summary>Gets the upper bound of the slider range.</summary>
    public double Maximum => _maximum;

    /// <summary>Gets the step increment the value is snapped to.</summary>
    public double Step => _step;

    /// <summary>Gets the track orientation (horizontal maps left→right, vertical maps bottom→top).</summary>
    public WpfOrientation Orientation => _orientation;

    /// <summary>
    /// Gets or sets the current slider value, clamped into the range and snapped to
    /// <see cref="Step"/>. The template binds this two-way; a change made while dragging emits a
    /// throttled action (M2-RF-402).
    /// </summary>
    public double Value
    {
        get => _value;
        set => SetValue(value);
    }

    /// <summary>Gets the display text for the current value.</summary>
    public string ValueText => _value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the command invoked when the drag/tap gesture completes (M2-RF-402). The template's
    /// slider raises it once per completed interaction so the final value reaches the action
    /// exactly once without a duplicate of the last emitted value.
    /// </summary>
    public ICommand CompleteInteractionCommand { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Re-sanitizes the range/step and keeps the currently displayed value clamped and snapped into
    /// the edited range (M2-RF-433), so a range change is reflected live without a rebuild. A change
    /// of the configured initial value (M2-RF-433/D-M2-6) is instead projected once onto the live
    /// value, which is the startup state; editing any other property leaves the transient runtime
    /// value in place. The value itself is not persisted here; the configuration keeps the edited
    /// initial value and the runtime position is never written back.
    /// </remarks>
    public override void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        base.ApplyConfig(config);

        if (config is not SliderWidgetConfig slider)
        {
            return;
        }

        (_minimum, _maximum, _step) = Sanitize(slider.Minimum, slider.Maximum, slider.Step);
        _orientation = slider.Orientation == SliderOrientation.Vertical
            ? WpfOrientation.Vertical
            : WpfOrientation.Horizontal;

        // M2-RF-433/D-M2-6: a committed edit of the configured initial value projects onto the live
        // value (the startup state) exactly once; the transient runtime value from dragging is not
        // captured and is preserved across any other projection, re-clamped into the edited range.
        if (!slider.Value.Equals(_configuredValue))
        {
            _configuredValue = slider.Value;
            _value = Snap(slider.Value);
        }
        else
        {
            _value = Snap(_value);
        }

        _supportsValueBinding = EvaluateBindingSupport();

        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(Maximum));
        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(Orientation));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));
    }

    /// <summary>
    /// Emits the final value for a completed interaction (M2-RF-402). A no-op when the value was
    /// already emitted (no duplicate) or when the widget has no supported value binding.
    /// </summary>
    /// <param name="cancellationToken">Cancels the in-flight execution.</param>
    /// <returns>A task that completes when the emission finished.</returns>
    public Task CompleteInteractionAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEmit || (_lastEmittedValue is double last && last.Equals(_value)))
        {
            return Task.CompletedTask;
        }

        return EmitAsync(cancellationToken);
    }

    private void SetValue(double raw)
    {
        var snapped = Snap(raw);
        if (snapped.Equals(_value))
        {
            return;
        }

        _value = snapped;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));

        TryEmitThrottled();
    }

    private void TryEmitThrottled()
    {
        if (!CanEmit)
        {
            return;
        }

        // M2-RF-402: at most one emission per emission-throttle window AND per step change; the
        // first emission of an interaction is always allowed.
        if (_lastEmittedValue is not null && _clock.GetUtcNow() - _lastEmitAt < EmissionThrottle)
        {
            return;
        }

        _ = EmitAsync();
    }

    private Task EmitAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEmit)
        {
            return Task.CompletedTask;
        }

        _lastEmittedValue = _value;
        _lastEmitAt = _clock.GetUtcNow();

        return ExecuteActionAsync(BuildVolumeSetAction(), cancellationToken);
    }

    private VolumeActionConfig BuildVolumeSetAction() => new()
    {
        Mode = VolumeMode.Set,
        Level = MapToVolumeLevel(_value),
    };

    private int MapToVolumeLevel(double value)
    {
        var span = _maximum - _minimum;
        if (!double.IsFinite(span) || span <= 0)
        {
            return ConfigValidator.MinVolumeLevel;
        }

        var fraction = Math.Clamp((value - _minimum) / span, 0d, 1d);
        var level = (int)Math.Round(fraction * 100d, MidpointRounding.AwayFromZero);
        return Math.Clamp(level, ConfigValidator.MinVolumeLevel, ConfigValidator.MaxVolumeLevel);
    }

    /// <summary>
    /// Determines whether the bound action exposes the numeric parameter the slider drives
    /// (M2-RF-403). A missing action leaves the slider enabled as display-only; any action other
    /// than <see cref="VolumeMode.Set"/> disables the widget with a visible indicator and a log.
    /// </summary>
    /// <returns><see langword="true"/> when the slider may emit its value.</returns>
    private bool EvaluateBindingSupport()
    {
        var action = BoundAction;
        if (action is null)
        {
            return false;
        }

        if (action is VolumeActionConfig { Mode: VolumeMode.Set })
        {
            return true;
        }

        var reason =
            $"Slider '{WidgetId}' binds action '{action.Type}', which has no numeric parameter; "
            + "only the volume 'Set' action is supported, so the slider was disabled.";

        if (IsEnabled)
        {
            IsEnabled = false;
            DisabledReason = reason;
        }

        _logger.Warning(
            "Slider {WidgetId}: unsupported action binding {ActionType}; the slider was disabled",
            WidgetId,
            action.Type);

        return false;
    }

    private bool CanEmit => _supportsValueBinding && IsEnabled;

    private double Snap(double value)
    {
        var clamped = double.IsFinite(value) ? Math.Clamp(value, _minimum, _maximum) : _minimum;
        var steps = Math.Round((clamped - _minimum) / _step, MidpointRounding.AwayFromZero);
        var snapped = _minimum + (steps * _step);
        return Math.Clamp(snapped, _minimum, _maximum);
    }

    private static (double Minimum, double Maximum, double Step) Sanitize(
        double minimum,
        double maximum,
        double step)
    {
        // The validator normalizes these at load time; this mirrors ConfigValidator so a
        // programmatic caller cannot produce a non-finite or zero-span track.
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum - minimum <= 0)
        {
            minimum = 0;
            maximum = 100;
        }

        var span = maximum - minimum;
        if (!double.IsFinite(step) || step <= 0 || step > span)
        {
            step = span < 1 ? span : 1;
        }

        return (minimum, maximum, step);
    }
}
