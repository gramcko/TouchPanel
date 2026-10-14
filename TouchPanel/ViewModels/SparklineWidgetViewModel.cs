using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;

namespace TouchPanel.ViewModels;

/// <summary>
/// Sparkline/graph widget view model (T-07, RF-041/M2-RF-411..416): renders the monitoring layer's
/// bounded per-metric history on the UI thread. It reads only the read-only
/// <see cref="ISensorHistory"/> boundary and never touches the hardware library or the polling path
/// — the store is fed by <c>MonitoringService.PublishOnce</c>, which this widget never calls
/// (M2-RF-412).
/// </summary>
/// <remarks>
/// <para><b>History only (M2-RF-412/M2-RF-415).</b> The rendered points are the newest slice of the
/// history returned by <see cref="ISensorHistory.GetHistory(string)"/> that fits this widget's own
/// <see cref="WindowSeconds"/> (<c>ceil(window / interval)</c> samples), so widgets with different
/// windows each render correctly from one shared, maximally-sized store; the widget keeps no
/// collection of its own and the slice length never exceeds the store's fixed capacity. The
/// configured <see cref="WindowSeconds"/> bounds the retained history through the monitoring layer's
/// capacity reconciliation, not through a second buffer here.</para>
/// <para><b>Gaps (M2-RF-414).</b> An unavailable sample is a <see langword="null"/> entry and is
/// rendered as a gap, never as zero; auto-scaling ignores unavailable samples and a window with no
/// available sample reports <see cref="IsAvailable"/> = <see langword="false"/> so the template
/// shows the explicit <see cref="UnavailableText"/> state.</para>
/// <para><b>Rate discipline (M2-RF-413).</b> The view model subscribes to
/// <see cref="ISensorSnapshotSource.SnapshotAvailable"/> only as a change signal (at most once per
/// snapshot, ~1 Hz), re-reads the history, and raises no notifications when the slice and scale are
/// unchanged. There is no idle animation and no timer.</para>
/// <para><b>Threading (M2-RF-413/RNF-004).</b> Snapshot callbacks arrive on the monitoring
/// background thread; the history is read there (a consistent copy under the store lock) and the
/// immutable result is marshalled through the base dispatcher hook so UI state only mutates on the
/// dispatcher.</para>
/// </remarks>
public sealed class SparklineWidgetViewModel : WidgetViewModelBase
{
    /// <summary>The text shown when the configured sensor has no available sample (M2-RF-414).</summary>
    public const string UnavailableText = "Unavailable";

    private readonly ISensorHistory? _history;
    private readonly ISensorSnapshotSource? _snapshotSource;
    private readonly ILogger _logger;
    private readonly Action<string>? _requestTracking;
    private string _sensorKey;
    private double _windowSeconds;
    private bool _isFilled;
    private bool _showValue;
    private DisplayUnit _displayUnit = DisplayUnit.Auto;
    private SensorUnit _unit = SensorUnit.Percent;
    private string _valueText = UnavailableText;
    private double? _configuredMinimum;
    private double? _configuredMaximum;
    private IReadOnlyList<double?> _points = Array.Empty<double?>();
    private double _scaleMinimum;
    private double _scaleMaximum = 100d;
    private bool _isAvailable;
    private bool _renderFailureLogged;
    private bool _disabledByUnknownSensor;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SparklineWidgetViewModel"/> class, requests
    /// tracking of its metric through the supplied history-tracking seam (M2-RF-693), reads the
    /// current slice, and subscribes to snapshot notifications as its change signal (M2-RF-412).
    /// </summary>
    /// <param name="config">The validated sparkline config; must not be <see langword="null"/>.</param>
    /// <param name="history">The read-only bounded history; <see langword="null"/> leaves the widget permanently unavailable.</param>
    /// <param name="snapshotSource">The snapshot boundary used only to know when a new sample was produced; <see langword="null"/> renders the initial slice only.</param>
    /// <param name="requestTracking">Requests a history buffer for the metric through the monitoring-owned tracking surface (M2-RF-693); may be <see langword="null"/> in tests.</param>
    /// <param name="timeProvider">Clock used by the base error-state support; unused by the sparkline.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for the unknown-sensor and render-failure notices.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public SparklineWidgetViewModel(
        SparklineWidgetConfig config,
        ISensorHistory? history = null,
        ISensorSnapshotSource? snapshotSource = null,
        Action<string>? requestTracking = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.Sparkline, actionExecutor: null, timeProvider, dispatch, logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        _sensorKey = config.Sensor?.Trim() ?? string.Empty;
        _history = history;
        _snapshotSource = snapshotSource;
        _logger = logger ?? Log.Logger;
        _requestTracking = requestTracking;
        _windowSeconds = SanitizeWindow(config.WindowSeconds);
        _isFilled = config.Style == SparklineStyle.Filled;
        _showValue = config.ShowValue;
        _displayUnit = NormalizeDisplayUnit(config.DisplayUnit);
        _unit = ResolveUnit();
        _configuredMinimum = config.Minimum;
        _configuredMaximum = config.Maximum;

        ReconcileSensor();

        Refresh();

        if (_snapshotSource is { } source)
        {
            source.SnapshotAvailable += OnSnapshotAvailable;
        }
    }

    /// <summary>Gets the configured history window in seconds (bounded to the RF-041 range).</summary>
    public double WindowSeconds => _windowSeconds;

    /// <summary>Gets the trimmed application-level sensor key this sparkline graphs.</summary>
    public string SensorKey => _sensorKey;

    /// <summary>Gets a value indicating whether the configured render style is the filled variant.</summary>
    public bool IsFilled => _isFilled;

    /// <summary>
    /// Gets a value indicating whether the current value is shown as text in the header
    /// (user-requested addition). Defaults to <see langword="false"/> (graph only).
    /// </summary>
    public bool ShowValue => _showValue;

    /// <summary>
    /// Gets the formatted current (latest) value, or <see cref="UnavailableText"/> when the newest
    /// retained sample is missing (user-requested addition). Values are formatted with the sensor's
    /// unit and the configured <see cref="DisplayUnit"/>; the text is independent of whether
    /// <see cref="ShowValue"/> is enabled.
    /// </summary>
    public string ValueText => _valueText;

    /// <summary>
    /// Gets the retained samples, oldest → newest, with <see langword="null"/> entries marking
    /// unavailable gaps (M2-RF-414). The count never exceeds the monitoring store's fixed capacity
    /// (M2-RF-415).
    /// </summary>
    public IReadOnlyList<double?> Points => _points;

    /// <summary>Gets the lower bound of the resolved vertical scale.</summary>
    public double ScaleMinimum => _scaleMinimum;

    /// <summary>Gets the upper bound of the resolved vertical scale.</summary>
    public double ScaleMaximum => _scaleMaximum;

    /// <summary>
    /// Gets a value indicating whether the window contains at least one available sample. When
    /// <see langword="false"/> the template shows <see cref="UnavailableText"/> instead of a plot.
    /// </summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Re-reads the bounded history and applies it. Safe to call from any thread; the slice is a
    /// consistent copy and state changes are marshalled to the UI thread when a dispatcher is
    /// supplied. A no-op after disposal.
    /// </summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        IReadOnlyList<double?> points;
        try
        {
            points = ReadPoints();
        }
        catch (Exception ex)
        {
            ApplyRenderFailure(ex);
            return;
        }

        ApplyPoints(points);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Refreshes the sensor key, window, style, and scale live (M2-RF-433), re-requests history
    /// tracking (M2-RF-693), re-evaluates the unknown-sensor containment (RF-047), and re-reads the
    /// bounded history — all without a rebuild.
    /// </remarks>
    public override void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        base.ApplyConfig(config);

        if (config is not SparklineWidgetConfig sparkline)
        {
            return;
        }

        _sensorKey = sparkline.Sensor?.Trim() ?? string.Empty;
        _windowSeconds = SanitizeWindow(sparkline.WindowSeconds);
        _isFilled = sparkline.Style == SparklineStyle.Filled;
        _showValue = sparkline.ShowValue;
        _displayUnit = NormalizeDisplayUnit(sparkline.DisplayUnit);
        _unit = ResolveUnit();
        _configuredMinimum = sparkline.Minimum;
        _configuredMaximum = sparkline.Maximum;

        ReconcileSensor();

        OnPropertyChanged(nameof(SensorKey));
        OnPropertyChanged(nameof(WindowSeconds));
        OnPropertyChanged(nameof(IsFilled));
        OnPropertyChanged(nameof(ShowValue));
        Refresh();
    }

    /// <summary>
    /// Re-evaluates the unknown-sensor containment (RF-047) after a sensor edit: an unknown key
    /// disables the widget (logged once per resolved key), a known key restores it, and tracking is
    /// requested through the monitoring-owned surface (M2-RF-693).
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
                    "Sparkline {WidgetId}: sensor '{SensorKey}' is not a known sensor key; the widget was disabled",
                    WidgetId,
                    _sensorKey);
            }

            if (IsEnabled)
            {
                IsEnabled = false;
                DisabledReason = $"Sparkline '{WidgetId}': sensor '{_sensorKey}' is not a known sensor key.";
                NotifyEnablement();
            }

            return;
        }

        RestoreFromUnknownSensor();

        if (_requestTracking is null)
        {
            return;
        }

        try
        {
            _requestTracking(_sensorKey);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Sparkline {WidgetId}: requesting history tracking failed", WidgetId);
        }
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

    /// <inheritdoc />
    protected override void OnDispose()
    {
        _disposed = true;
        if (_snapshotSource is { } source)
        {
            source.SnapshotAvailable -= OnSnapshotAvailable;
        }
    }

    private void OnSnapshotAvailable(SensorSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        IReadOnlyList<double?> points;
        try
        {
            // The store returns an immutable copy under its lock, so this read may happen on the
            // monitoring thread; only the resulting state mutation is marshalled (M2-RF-413).
            points = ReadPoints();
        }
        catch (Exception ex)
        {
            PostToUi(() => ApplyRenderFailure(ex));
            return;
        }

        PostToUi(() => ApplyPoints(points));
    }

    private IReadOnlyList<double?> ReadPoints()
    {
        if (_history is null || _sensorKey.Length == 0)
        {
            return Array.Empty<double?>();
        }

        return SliceToWindow(_history.GetHistory(_sensorKey), _history.Interval);
    }

    /// <summary>
    /// Returns the newest samples that fit this widget's own window from the shared, maximally-sized
    /// store buffer (M2-RF-415): the point count is
    /// <c>ceil(WindowSeconds / interval)</c>, so two sparklines with different windows each render
    /// the correct slice of the one bounded store. The result is at most the store capacity, so it
    /// stays bounded; historical gap entries are preserved unchanged.
    /// </summary>
    /// <param name="history">The full retained history (oldest → newest).</param>
    /// <param name="interval">The store's polling interval.</param>
    /// <returns>The newest samples for this widget's window.</returns>
    private IReadOnlyList<double?> SliceToWindow(IReadOnlyList<double?> history, TimeSpan interval)
    {
        if (history.Count == 0 || interval <= TimeSpan.Zero)
        {
            return history;
        }

        var windowCount = SensorHistoryStore.ComputeCapacity(_windowSeconds, interval);
        if (windowCount >= history.Count)
        {
            return history;
        }

        var slice = new double?[windowCount];
        var offset = history.Count - windowCount;
        for (var i = 0; i < windowCount; i++)
        {
            slice[i] = history[offset + i];
        }

        return slice;
    }

    private void ApplyPoints(IReadOnlyList<double?> points)
    {
        if (_disposed)
        {
            return;
        }

        var (scaleMinimum, scaleMaximum, isAvailable) = ResolveScale(points);
        var valueText = ComputeValueText(points);

        if (SequenceEquals(_points, points)
            && _scaleMinimum.Equals(scaleMinimum)
            && _scaleMaximum.Equals(scaleMaximum)
            && _isAvailable == isAvailable
            && string.Equals(_valueText, valueText, StringComparison.Ordinal))
        {
            // M2-RF-413: no re-render when the history, scale, and value text are unchanged.
            return;
        }

        _points = points;
        _scaleMinimum = scaleMinimum;
        _scaleMaximum = scaleMaximum;
        _isAvailable = isAvailable;
        _valueText = valueText;

        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(ScaleMinimum));
        OnPropertyChanged(nameof(ScaleMaximum));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(ValueText));
    }

    private void ApplyRenderFailure(Exception exception)
    {
        if (_disposed)
        {
            return;
        }

        if (!_renderFailureLogged)
        {
            // M2-RF-416: a render/scale failure degrades to "Unavailable" and logs once; it never
            // crashes the page or affects other widgets (RF-012/RNF-013).
            _renderFailureLogged = true;
            _logger.Warning(
                exception,
                "Sparkline {WidgetId}: rendering the history failed; showing unavailable",
                WidgetId);
        }

        var previousAvailable = _isAvailable;
        var hadPoints = _points.Count != 0;
        var hadValueText = !string.Equals(_valueText, UnavailableText, StringComparison.Ordinal);
        _points = Array.Empty<double?>();
        _isAvailable = false;
        _valueText = UnavailableText;

        if (hadPoints)
        {
            OnPropertyChanged(nameof(Points));
        }

        if (previousAvailable)
        {
            OnPropertyChanged(nameof(IsAvailable));
        }

        if (hadValueText)
        {
            OnPropertyChanged(nameof(ValueText));
        }
    }

    /// <summary>
    /// Formats the newest retained sample for the header readout (user-requested addition): the
    /// sensor's unit and the configured <see cref="DisplayUnit"/> drive the text, and a missing or
    /// non-finite newest sample degrades to <see cref="UnavailableText"/> (never a fabricated zero).
    /// </summary>
    /// <param name="points">The retained samples (gaps are <see langword="null"/>).</param>
    /// <returns>The formatted current value or <see cref="UnavailableText"/>.</returns>
    private string ComputeValueText(IReadOnlyList<double?> points)
    {
        if (points.Count == 0 || points[^1] is not { } latest || !double.IsFinite(latest))
        {
            return UnavailableText;
        }

        return GaugeWidgetViewModel.FormatValue(latest, _unit, _displayUnit);
    }

    /// <summary>
    /// Resolves the configured sensor key to its unit without touching hardware (the same
    /// application-level key table the snapshot boundary uses). An unknown or empty key falls back to
    /// <see cref="SensorUnit.Percent"/>; the widget is disabled for unknown keys anyway.
    /// </summary>
    /// <returns>The sensor unit used to format the current value.</returns>
    private SensorUnit ResolveUnit() => _sensorKey.Length > 0
        && SensorSnapshot.CreateUnavailable(DateTimeOffset.UnixEpoch).TryGetReading(_sensorKey, out var reading)
            ? reading.Unit
            : SensorUnit.Percent;

    private static DisplayUnit NormalizeDisplayUnit(DisplayUnit displayUnit) =>
        displayUnit == DisplayUnit.Unknown ? DisplayUnit.Auto : displayUnit;

    /// <summary>
    /// Resolves the vertical scale (M2-RF-411): an explicit valid <c>minimum</c>/<c>maximum</c> pair
    /// wins; otherwise the range spans the finite samples, with a small padding for a flat line so
    /// it is centered rather than drawn on the axis. An all-unavailable window has no available
    /// sample and yields the neutral 0–100 range.
    /// </summary>
    /// <param name="points">The retained samples (gaps are <see langword="null"/>).</param>
    /// <returns>The scale bounds and whether any sample is available.</returns>
    private (double Minimum, double Maximum, bool Available) ResolveScale(IReadOnlyList<double?> points)
    {
        var hasAny = false;
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;

        foreach (var sample in points)
        {
            if (sample is not { } value || !double.IsFinite(value))
            {
                continue;
            }

            hasAny = true;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        if (TryConfiguredScale(out var configuredMinimum, out var configuredMaximum))
        {
            return (configuredMinimum, configuredMaximum, hasAny);
        }

        if (!hasAny)
        {
            return (0d, 100d, false);
        }

        var span = maximum - minimum;
        if (span < ConfigValidator.MinGaugeSpan)
        {
            // Center a flat/short line within a symmetric band so the stroke is visible.
            var middle = (maximum + minimum) / 2d;
            var padding = Math.Max(ConfigValidator.MinGaugeSpan, Math.Abs(middle) * 0.05d);
            return (middle - padding, middle + padding, true);
        }

        return (minimum, maximum, true);
    }

    private bool TryConfiguredScale(out double minimum, out double maximum)
    {
        minimum = 0d;
        maximum = 0d;

        if (_configuredMinimum is not double candidateMinimum
            || _configuredMaximum is not double candidateMaximum
            || !double.IsFinite(candidateMinimum)
            || !double.IsFinite(candidateMaximum)
            || candidateMaximum - candidateMinimum < ConfigValidator.MinGaugeSpan)
        {
            return false;
        }

        minimum = candidateMinimum;
        maximum = candidateMaximum;
        return true;
    }

    private static bool SequenceEquals(IReadOnlyList<double?> left, IReadOnlyList<double?> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!Nullable.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static double SanitizeWindow(double windowSeconds)
    {
        // The validator clamps this at load time; mirror it defensively for programmatic callers.
        if (!double.IsFinite(windowSeconds))
        {
            return SparklineWidgetConfig.DefaultWindowSeconds;
        }

        return Math.Clamp(
            windowSeconds,
            SparklineWidgetConfig.MinWindowSeconds,
            SparklineWidgetConfig.MaxWindowSeconds);
    }
}
