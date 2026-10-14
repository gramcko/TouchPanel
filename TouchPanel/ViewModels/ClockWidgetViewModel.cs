using System.Globalization;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Time;

namespace TouchPanel.ViewModels;

/// <summary>
/// Clock widget view model (RF-046/M3-RF-462/463): a display-only local time/date driven by a
/// preset <see cref="ClockMode"/> or an optional bounded custom format. The displayed text is
/// recomputed by a single <see cref="TimeProvider"/> timer that fires exactly on the next boundary
/// (the next whole minute, or the next whole second when the effective format contains a real
/// seconds/sub-second specifier), and the boundary is recomputed after every tick and whenever the
/// injected <see cref="IClockStateSource"/> reports a clock/time-zone/power/session change.
/// </summary>
/// <remarks>
/// <para><b>Cadence (M3-RF-463/M3-RNF-131).</b> The refresh rate is decided once per effective
/// format by <see cref="ClockFormat.RequiresPerSecondRefresh(string)"/>; the <c>t</c>/<c>tt</c>
/// AM/PM designators never force per-second updates and escaped/quoted literals are ignored. No
/// idle busy loop and no animation run, and the widget raises a change notification only when the
/// displayed text actually changes.</para>
/// <para><b>Determinism (M3-RNF-133).</b> The display culture is pinned at construction and the
/// time zone is read through the injected <see cref="TimeProvider"/> (or an explicit override), so
/// tests are fully deterministic. A timer or format failure degrades to the last-valid label and
/// logs; it never crashes the page (RF-012/M3-RNF-138).</para>
/// </remarks>
public sealed class ClockWidgetViewModel : WidgetViewModelBase
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CultureInfo _culture;
    private readonly TimeZoneInfo? _timeZone;
    private readonly IClockStateSource? _clockStateSource;
    private ClockMode _mode;
    private string? _configuredFormat;
    private string _effectiveFormat = string.Empty;
    private bool _perSecond;
    private string _displayText = string.Empty;
    private ITimer? _timer;
    private bool _formatWarningLogged;
    private bool _tickFailureLogged;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClockWidgetViewModel"/> class, resolves the
    /// effective format, displays the current instant, and schedules the first boundary tick.
    /// </summary>
    /// <param name="config">The validated clock config; must not be <see langword="null"/>.</param>
    /// <param name="clockStateSource">The OS clock/state seam; <see langword="null"/> disables event-driven recomputation.</param>
    /// <param name="timeProvider">The clock/timer source; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for invalid-format and timer-failure notices.</param>
    /// <param name="culture">
    /// An explicit pinned display culture (tests); when set it wins over
    /// <paramref name="clockCulture"/>. Prefer <paramref name="clockCulture"/> in production so the
    /// validator and the widget share the one canonical source (M4-RF-951).
    /// </param>
    /// <param name="timeZone">An explicit display time zone; <see langword="null"/> uses the provider's local zone.</param>
    /// <param name="clockCulture">
    /// The canonical Clock culture source (M4-RF-951) supplied by the composition root; when
    /// <see langword="null"/> (and no explicit <paramref name="culture"/> is given) the process
    /// default <see cref="ClockCultureSource.Default"/> is used, matching the validator's fallback.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public ClockWidgetViewModel(
        ClockWidgetConfig config,
        IClockStateSource? clockStateSource = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null,
        CultureInfo? culture = null,
        TimeZoneInfo? timeZone = null,
        IClockCultureSource? clockCulture = null)
        : base(config, WidgetTemplateKeys.Clock, actionExecutor: null, timeProvider, dispatch, logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? Log.Logger;
        _culture = culture ?? clockCulture?.Culture ?? ClockCultureSource.Default.Culture;
        _timeZone = timeZone;
        _clockStateSource = clockStateSource;
        _mode = config.Mode;
        _configuredFormat = config.Format;

        RecomputeFormat();

        try
        {
            UpdateText();
        }
        catch (Exception exception)
        {
            LogTickFailure(exception);
        }

        if (clockStateSource is not null)
        {
            clockStateSource.StateChanged += OnClockStateChanged;
        }

        ScheduleNext();
    }

    /// <summary>Gets the current display text (local time/date formatted with the effective format).</summary>
    public string DisplayText => _displayText;

    /// <summary>Gets the effective format actually used (custom when valid, otherwise the <see cref="ClockMode"/> preset).</summary>
    public string EffectiveFormat => _effectiveFormat;

    /// <summary>Gets a value indicating whether the effective format forces a per-second refresh.</summary>
    public bool RequiresPerSecond => _perSecond;

    /// <summary>Gets a value indicating whether a non-empty label is configured.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <inheritdoc />
    /// <remarks>
    /// Refreshes the mode/format live (base properties included), then recomputes the effective
    /// format, the displayed text, and the next boundary without a rebuild.
    /// </remarks>
    public override void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        base.ApplyConfig(config);

        if (config is not ClockWidgetConfig clock)
        {
            return;
        }

        _mode = clock.Mode;
        _configuredFormat = clock.Format;
        _formatWarningLogged = false;
        RecomputeFormat();

        try
        {
            UpdateText();
        }
        catch (Exception exception)
        {
            LogTickFailure(exception);
        }

        ScheduleNext();
        OnPropertyChanged(nameof(EffectiveFormat));
        OnPropertyChanged(nameof(RequiresPerSecond));
        OnPropertyChanged(nameof(HasLabel));
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;

        if (_clockStateSource is { } source)
        {
            source.StateChanged -= OnClockStateChanged;
        }
    }

    private void OnClockStateChanged() => PostToUi(() =>
    {
        if (_disposed)
        {
            return;
        }

        // The system clock/time zone changed or the machine resumed: refresh the instant and the
        // next boundary rather than waiting for the stale timer.
        try
        {
            UpdateText();
        }
        catch (Exception exception)
        {
            LogTickFailure(exception);
        }

        ScheduleNext();
    });

    private void RecomputeFormat()
    {
        if (ClockFormat.TryCustomFormat(_configuredFormat, _culture))
        {
            _effectiveFormat = _configuredFormat!;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_configuredFormat) && !_formatWarningLogged)
            {
                _formatWarningLogged = true;
                _logger.Warning(
                    "Clock {WidgetId}: the custom format was invalid or over-length; using the {Mode} preset",
                    WidgetId,
                    _mode);
            }

            _effectiveFormat = ClockFormat.PresetFormat(_mode);
        }

        _perSecond = ClockFormat.RequiresPerSecondRefresh(_effectiveFormat);
    }

    private bool UpdateText()
    {
        var text = Now().ToString(_effectiveFormat, _culture);
        if (string.Equals(_displayText, text, StringComparison.Ordinal))
        {
            return false;
        }

        _displayText = text;
        OnPropertyChanged(nameof(DisplayText));
        return true;
    }

    private void ScheduleNext()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _timer?.Dispose();
            _timer = _timeProvider.CreateTimer(
                OnTimer,
                state: null,
                dueTime: ClockFormat.ComputeNextBoundary(Now(), _perSecond),
                period: Timeout.InfiniteTimeSpan);
        }
        catch (Exception exception)
        {
            LogTickFailure(exception);
        }
    }

    private void OnTimer(object? state) => PostToUi(() =>
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            UpdateText();
        }
        catch (Exception exception)
        {
            LogTickFailure(exception);
        }

        // Recompute the next boundary after every tick so the clock never drifts.
        ScheduleNext();
    });

    private DateTimeOffset Now() =>
        _timeZone is { } zone
            ? TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), zone)
            : _timeProvider.GetLocalNow();

    private void LogTickFailure(Exception exception)
    {
        if (_tickFailureLogged)
        {
            return;
        }

        _tickFailureLogged = true;
        _logger.Warning(
            exception,
            "Clock {WidgetId}: formatting or scheduling the tick failed; keeping the last valid text",
            WidgetId);
    }
}
