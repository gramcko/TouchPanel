using Serilog;
using TouchPanel.Config;
using TouchPanel.Monitoring;

namespace TouchPanel.ViewModels;

/// <summary>
/// Bar widget view model (RF-046/M3-RF-466/467): a single-value horizontal meter that resolves the
/// configured stable sensor key against published <see cref="SensorSnapshot"/> values, maps the
/// value onto the configured <see cref="Minimum"/>/<see cref="Maximum"/> range, and shows an
/// explicit "Unavailable" state (never a fabricated zero) when the sensor or hardware is missing
/// (RF-063/RF-065).
/// </summary>
/// <remarks>
/// <para><b>Rate discipline (M3-RF-467/M3-RNF-131).</b> The bar is event-driven: it only reacts to
/// <see cref="ISensorSnapshotSource.SnapshotAvailable"/> (at most once per snapshot, ~1 Hz),
/// suppresses notifications when the value/availability did not change, never polls hardware, and
/// never reads the bounded history.</para>
/// <para><b>Threading (M3-RNF-132).</b> Snapshot callbacks arrive on the monitoring background
/// thread; the resolved state is marshalled through the base dispatcher hook so UI mutation only
/// happens on the dispatcher.</para>
/// </remarks>
public sealed class BarWidgetViewModel : WidgetViewModelBase
{
    /// <summary>The text shown when the configured sensor has no value (M3-RF-467).</summary>
    public const string UnavailableText = "Unavailable";

    private readonly ISensorSnapshotSource? _snapshotSource;
    private readonly ILogger _logger;
    private readonly object _observeGate = new();
    private string _sensorKey;
    private DisplayUnit _displayUnit = DisplayUnit.Auto;
    private double? _lastObservedValue; // owned by the (single) monitoring producer after construction
    private double? _value;             // UI-thread state
    private string _valueText = UnavailableText;
    private double _progress;
    private bool _isAvailable;
    private bool _showValue;
    private bool _unknownSensorLogged;
    private bool _disabledByUnknownSensor;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BarWidgetViewModel"/> class and subscribes to the
    /// shared snapshot source, applying the latest snapshot when one already exists.
    /// </summary>
    /// <param name="config">The validated bar config (sensor, minimum, maximum, showValue).</param>
    /// <param name="snapshotSource">The read-only snapshot boundary; <see langword="null"/> leaves the bar permanently unavailable.</param>
    /// <param name="timeProvider">Clock used by the base error-state support; unused by bars.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for the one-time unknown-sensor notice.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public BarWidgetViewModel(
        BarWidgetConfig config,
        ISensorSnapshotSource? snapshotSource = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.Bar, actionExecutor: null, timeProvider, dispatch, logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        _sensorKey = config.Sensor?.Trim() ?? string.Empty;
        _logger = logger ?? Log.Logger;
        _snapshotSource = snapshotSource;
        _showValue = config.ShowValue;
        _displayUnit = NormalizeDisplayUnit(config.DisplayUnit);

        var (minimum, maximum) = SanitizeRange(config.Minimum, config.Maximum);
        Minimum = minimum;
        Maximum = maximum;

        ReconcileSensor();

        if (snapshotSource is not null)
        {
            snapshotSource.SnapshotAvailable += OnSnapshotAvailable;

            if (snapshotSource.Latest is { } latest)
            {
                ApplySnapshot(latest);
            }
        }
    }

    /// <summary>Gets the lower bound of the configured display range.</summary>
    public double Minimum { get; private set; }

    /// <summary>Gets the upper bound of the configured display range.</summary>
    public double Maximum { get; private set; }

    /// <summary>Gets a value indicating whether the numeric value is shown beside the meter.</summary>
    public bool ShowValue => _showValue;

    /// <summary>Gets the resolved raw sensor value, or <see langword="null"/> when unavailable.</summary>
    public double? Value => _value;

    /// <summary>Gets the formatted value text, or <see cref="UnavailableText"/> when unavailable.</summary>
    public string ValueText => _valueText;

    /// <summary>Gets a value indicating whether a real sensor value is currently displayed.</summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>Gets the value mapped onto 0–100 within the configured range, clamped to its bounds.</summary>
    public double Progress => _progress;

    /// <summary>
    /// Resolves and applies one snapshot. Safe to call from the monitoring background thread; state
    /// changes are marshalled to the UI thread. Identical values produce no notifications (M3-RF-467).
    /// </summary>
    /// <param name="snapshot">The snapshot to resolve the configured sensor against.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is <see langword="null"/>.</exception>
    public void ApplySnapshot(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        double? value = null;
        var unit = SensorUnit.Percent;
        var hasReading = false;
        var reading = default(SensorReading);

        if (_sensorKey.Length > 0)
        {
            hasReading = snapshot.TryGetReading(_sensorKey, out reading);
        }

        if (hasReading)
        {
            unit = reading.Unit;
            value = reading.IsAvailable ? reading.Value : null;
        }

        lock (_observeGate)
        {
            if (!hasReading && _sensorKey.Length > 0 && !_unknownSensorLogged)
            {
                _unknownSensorLogged = true;
                _logger.Warning(
                    "Bar {WidgetId}: sensor '{SensorKey}' is not a known sensor key; showing unavailable",
                    WidgetId,
                    _sensorKey);
            }

            if (Nullable.Equals(_lastObservedValue, value))
            {
                return;
            }

            _lastObservedValue = value;
        }

        PostToUi(() => ApplyReading(value, unit));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Refreshes the sensor key, display range, and showValue flag live, re-resolves the latest
    /// snapshot, and re-evaluates the unknown-sensor containment without a rebuild.
    /// </remarks>
    public override void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        base.ApplyConfig(config);

        if (config is not BarWidgetConfig bar)
        {
            return;
        }

        _sensorKey = bar.Sensor?.Trim() ?? string.Empty;
        _showValue = bar.ShowValue;
        _displayUnit = NormalizeDisplayUnit(bar.DisplayUnit);
        OnPropertyChanged(nameof(ShowValue));

        var (minimum, maximum) = SanitizeRange(bar.Minimum, bar.Maximum);
        var rangeChanged = !Minimum.Equals(minimum) || !Maximum.Equals(maximum);
        Minimum = minimum;
        Maximum = maximum;

        lock (_observeGate)
        {
            _lastObservedValue = null;
        }

        _unknownSensorLogged = false;
        ReconcileSensor();

        if (rangeChanged)
        {
            OnPropertyChanged(nameof(Minimum));
            OnPropertyChanged(nameof(Maximum));
            RecomputeProgress();
        }

        if (_snapshotSource?.Latest is { } latest)
        {
            ApplySnapshot(latest);
        }
        else if (_value is { } current)
        {
            ApplyReading(current, SensorUnit.Percent);
        }
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        _disposed = true;
        if (_snapshotSource is { } source)
        {
            source.SnapshotAvailable -= OnSnapshotAvailable;
        }
    }

    private void OnSnapshotAvailable(SensorSnapshot snapshot) => ApplySnapshot(snapshot);

    private void RecomputeProgress()
    {
        var progress = _isAvailable && _value is { } value ? ComputeProgress(value) : 0d;
        if (_progress.Equals(progress))
        {
            return;
        }

        _progress = progress;
        OnPropertyChanged(nameof(Progress));
    }

    private void ApplyReading(double? value, SensorUnit unit)
    {
        if (_disposed)
        {
            return;
        }

        var available = value is { } numeric && double.IsFinite(numeric);
        var newValue = available ? value : null;
        var newText = available ? GaugeWidgetViewModel.FormatValue(value!.Value, unit, _displayUnit) : UnavailableText;
        var newProgress = available ? ComputeProgress(value!.Value) : 0d;

        if (_isAvailable == available
            && Nullable.Equals(_value, newValue)
            && string.Equals(_valueText, newText, StringComparison.Ordinal))
        {
            return;
        }

        _isAvailable = available;
        _value = newValue;
        _valueText = newText;
        _progress = newProgress;

        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(Progress));
    }

    private double ComputeProgress(double value)
    {
        var span = Maximum - Minimum;
        if (!double.IsFinite(span) || span <= 0)
        {
            return 0d;
        }

        return Math.Clamp((value - Minimum) / span * 100d, 0d, 100d);
    }

    /// <summary>
    /// Re-evaluates the unknown-sensor containment (M3-RF-467): an unknown key disables the widget
    /// with a reason (logged once per resolved key) and a known key restores it.
    /// </summary>
    private void ReconcileSensor()
    {
        if (_sensorKey.Length == 0)
        {
            RestoreFromUnknownSensor();
            return;
        }

        var known = SensorSnapshot.CreateUnavailable(DateTimeOffset.UnixEpoch).TryGetReading(_sensorKey, out _);
        if (!known)
        {
            if (!_disabledByUnknownSensor)
            {
                _disabledByUnknownSensor = true;
                _logger.Warning(
                    "Bar {WidgetId}: sensor '{SensorKey}' is not a known sensor key; the widget was disabled",
                    WidgetId,
                    _sensorKey);
            }

            if (IsEnabled)
            {
                IsEnabled = false;
                DisabledReason = $"Bar '{WidgetId}': sensor '{_sensorKey}' is not a known sensor key.";
                NotifyEnablement();
            }

            return;
        }

        RestoreFromUnknownSensor();
    }

    private void RestoreFromUnknownSensor()
    {
        if (!_disabledByUnknownSensor)
        {
            return;
        }

        _disabledByUnknownSensor = false;
        IsEnabled = true;
        DisabledReason = null;
        NotifyEnablement();
    }

    private void NotifyEnablement()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(DisabledReason));
        OnPropertyChanged(nameof(ShowsDisabledIndicator));
        OnPropertyChanged(nameof(IsInteractable));
    }

    private static (double Minimum, double Maximum) SanitizeRange(double minimum, double maximum)
    {
        // The validator normalizes bar ranges, but a programmatic caller may bypass it; an invalid
        // range degrades to the 0–100 default instead of producing non-finite UI math.
        if (!double.IsFinite(minimum)
            || !double.IsFinite(maximum)
            || maximum <= minimum)
        {
            return (BarWidgetConfig.DefaultMinimum, BarWidgetConfig.DefaultMaximum);
        }

        return (minimum, maximum);
    }

    private static DisplayUnit NormalizeDisplayUnit(DisplayUnit displayUnit) =>
        displayUnit == DisplayUnit.Unknown ? DisplayUnit.Auto : displayUnit;
}
