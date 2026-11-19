using System.Globalization;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Monitoring;

namespace TouchPanel.ViewModels;

/// <summary>
/// Gauge widget view model (RF-033/RF-034/RF-038): resolves the configured stable sensor key
/// against published <see cref="SensorSnapshot"/> values, maps the value onto the configured
/// <see cref="Minimum"/>/<see cref="Maximum"/> range, and displays an explicit "Unavailable" state
/// (never a fabricated zero) when the sensor or the hardware library is missing (RF-063/RF-065).
/// </summary>
/// <remarks>
/// <para><b>Rate discipline (RF-038).</b> The gauge is event-driven: it only reacts to
/// <see cref="ISensorSnapshotSource.SnapshotAvailable"/> (at most ~1 Hz, RF-064) and suppresses
/// property notifications entirely when the resolved value did not change, so there is no idle
/// animation and no UI churn for steady values.</para>
/// <para><b>Threading (RF-067).</b> Snapshot callbacks arrive on the monitoring background thread;
/// all state mutation is marshalled through the base dispatcher hook and runs on the UI thread in
/// production. The gauge never calls the hardware library or the monitoring service.</para>
/// </remarks>
public sealed class GaugeWidgetViewModel : WidgetViewModelBase
{
    /// <summary>The text shown when the configured sensor has no value (RF-065).</summary>
    public const string UnavailableText = "Unavailable";

    private readonly ISensorSnapshotSource? _snapshotSource;
    private readonly ILogger _logger;
    private string _sensorKey;
    private DisplayUnit _displayUnit = DisplayUnit.Auto;
    private readonly object _observeGate = new();
    private double? _lastObservedValue; // owned by the (single) monitoring producer after construction
    private double? _value;             // UI-thread state
    private string _valueText = UnavailableText;
    private double _progress;
    private bool _isAvailable;
    private bool _unknownSensorLogged;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GaugeWidgetViewModel"/> class and subscribes to
    /// the shared snapshot source, applying the latest snapshot when one already exists.
    /// </summary>
    /// <param name="config">The validated gauge config (sensor, minimum, maximum).</param>
    /// <param name="snapshotSource">The read-only snapshot boundary; <see langword="null"/> leaves the gauge permanently unavailable.</param>
    /// <param name="timeProvider">Clock used by the base error-state support; unused by gauges.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for the one-time unknown-sensor notice.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public GaugeWidgetViewModel(
        GaugeWidgetConfig config,
        ISensorSnapshotSource? snapshotSource = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.Gauge, actionExecutor: null, timeProvider, dispatch, logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        _sensorKey = config.Sensor?.Trim() ?? string.Empty;
        _displayUnit = NormalizeDisplayUnit(config.DisplayUnit);
        _logger = logger ?? Log.Logger;
        _snapshotSource = snapshotSource;

        var (minimum, maximum) = SanitizeRange(config.Minimum, config.Maximum);
        Minimum = minimum;
        Maximum = maximum;

        if (snapshotSource is not null)
        {
            snapshotSource.SnapshotAvailable += OnSnapshotAvailable;

            // Apply the value captured before this gauge existed (page rebuilds, startup ordering).
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
    /// changes are marshalled to the UI thread. Identical values produce no notifications (RF-038).
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
                // One diagnostic per gauge instance; the widget itself stays usable and shows unavailable.
                _unknownSensorLogged = true;
                _logger.Warning(
                    "Gauge {WidgetId}: sensor '{SensorKey}' is not a known sensor key; showing unavailable",
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
    /// Refreshes the sensor key and the display range live (M2-RF-433): the latest snapshot is
    /// re-resolved so the value and progress follow the edited sensor/range without a rebuild. The
    /// observe gate is reset so an unchanged value is not suppressed.
    /// </remarks>
    public override void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        base.ApplyConfig(config);

        if (config is not GaugeWidgetConfig gauge)
        {
            return;
        }

        _sensorKey = gauge.Sensor?.Trim() ?? string.Empty;
        _displayUnit = NormalizeDisplayUnit(gauge.DisplayUnit);
        var (minimum, maximum) = SanitizeRange(gauge.Minimum, gauge.Maximum);
        var rangeChanged = !Minimum.Equals(minimum) || !Maximum.Equals(maximum);
        Minimum = minimum;
        Maximum = maximum;

        lock (_observeGate)
        {
            _lastObservedValue = null;
        }

        _unknownSensorLogged = false;

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
            // No source (tests/degraded startup): keep the displayed value but re-map progress to
            // the edited range.
            ApplyReading(current, SensorUnit.Percent);
        }
    }

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

    /// <inheritdoc />
    protected override void OnDispose()
    {
        _disposed = true;
        if (_snapshotSource is { } source)
        {
            source.SnapshotAvailable -= OnSnapshotAvailable;
        }
    }

    /// <summary>
    /// Formats a numeric reading using its unit (invariant culture so output is deterministic).
    /// Byte and byte-per-second readings honor the widget's <paramref name="displayUnit"/>
    /// (user-requested addition): <see cref="DisplayUnit.Auto"/> keeps the existing human-friendly
    /// formatting, while a fixed unit formats the value in that binary unit (rates keep the
    /// <c>/s</c> suffix). The unit is ignored for percent/temperature/RPM readings.
    /// </summary>
    /// <param name="value">The finite value to format.</param>
    /// <param name="unit">The unit of the value.</param>
    /// <param name="displayUnit">The configured byte/rate display unit; defaults to <see cref="DisplayUnit.Auto"/>.</param>
    /// <returns>The formatted display text.</returns>
    internal static string FormatValue(double value, SensorUnit unit, DisplayUnit displayUnit = DisplayUnit.Auto) => unit switch
    {
        SensorUnit.Percent => FormatNumber(value) + " %",
        SensorUnit.Bytes => FormatBytes(value, displayUnit, perSecond: false),
        SensorUnit.BytesPerSecond => FormatBytes(value, displayUnit, perSecond: true),
        SensorUnit.Celsius => FormatNumber(value) + " °C",
        SensorUnit.Rpm => value.ToString("0", CultureInfo.InvariantCulture) + " RPM",
        _ => FormatNumber(value),
    };

    private void OnSnapshotAvailable(SensorSnapshot snapshot) => ApplySnapshot(snapshot);

    private void ApplyReading(double? value, SensorUnit unit)
    {
        if (_disposed)
        {
            return;
        }

        var available = value is { } numeric && double.IsFinite(numeric);
        var newValue = available ? value : null;
        var newText = available ? FormatValue(value!.Value, unit, _displayUnit) : UnavailableText;
        var newProgress = available ? ComputeProgress(value!.Value) : 0d;

        if (_isAvailable == available && Nullable.Equals(_value, newValue) && string.Equals(_valueText, newText, StringComparison.Ordinal))
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

    private static (double Minimum, double Maximum) SanitizeRange(double minimum, double maximum)
    {
        // The validator normalizes gauge ranges, but a programmatic caller may bypass it; an
        // invalid range degrades to the 0–100 default instead of producing non-finite UI math.
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum - minimum < 1d)
        {
            return (0d, 100d);
        }

        return (minimum, maximum);
    }

    private static DisplayUnit NormalizeDisplayUnit(DisplayUnit displayUnit) =>
        displayUnit == DisplayUnit.Unknown ? DisplayUnit.Auto : displayUnit;

    private static string FormatNumber(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatBytes(double bytes, DisplayUnit displayUnit, bool perSecond)
    {
        if (displayUnit == DisplayUnit.Auto)
        {
            return FormatBytes(bytes) + (perSecond ? "/s" : string.Empty);
        }

        // User-requested addition: automatic binary KB/MB switching. Below 1024 KB the value stays
        // in KB (thousands are easier to read for slow traffic); at or above 1,048,576 bytes it
        // switches to MB, so a real network rate never shows thousands of KB. The threshold uses the
        // magnitude so a signed value switches symmetrically; zero renders as "0 KB".
        if (displayUnit == DisplayUnit.AutoKilobytes)
        {
            const double Kilobyte = 1024d;
            const double Megabyte = Kilobyte * 1024d;
            var autoText = Math.Abs(bytes) >= Megabyte
                ? FormatNumber(bytes / Megabyte) + " MB"
                : FormatNumber(bytes / Kilobyte) + " KB";
            return perSecond ? autoText + "/s" : autoText;
        }

        var (divisor, suffix) = displayUnit switch
        {
            DisplayUnit.Kilobytes => (1024d, "KB"),
            DisplayUnit.Megabytes => (1024d * 1024d, "MB"),
            DisplayUnit.Gigabytes => (1024d * 1024d * 1024d, "GB"),

            // Bytes and any other value (the unknown sentinel never reaches here after validation)
            // format in whole bytes.
            _ => (1d, "B"),
        };

        var text = FormatNumber(bytes / divisor) + " " + suffix;
        return perSecond ? text + "/s" : text;
    }

    private static string FormatBytes(double bytes)
    {
        const double Kilobyte = 1024d;
        const double Megabyte = Kilobyte * 1024d;
        const double Gigabyte = Megabyte * 1024d;
        const double Terabyte = Gigabyte * 1024d;

        var magnitude = Math.Abs(bytes);
        if (magnitude >= Terabyte)
        {
            return FormatNumber(bytes / Terabyte) + " TB";
        }

        if (magnitude >= Gigabyte)
        {
            return FormatNumber(bytes / Gigabyte) + " GB";
        }

        if (magnitude >= Megabyte)
        {
            return FormatNumber(bytes / Megabyte) + " MB";
        }

        if (magnitude >= Kilobyte)
        {
            return FormatNumber(bytes / Kilobyte) + " KB";
        }

        return bytes.ToString("0", CultureInfo.InvariantCulture) + " B";
    }
}
