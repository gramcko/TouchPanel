using System.Globalization;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Clock widget view model (RF-046/M3-RF-462/463): preset/custom format resolution with a safe
/// fallback and a logged warning, culture pinning, escaped-literal-aware cadence (minute vs
/// per-second), boundary recomputation on ticks and on injected OS clock-state events, and
/// update-on-change-only behavior — all deterministic through an injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class ClockWidgetTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PresetTime24_ShowsTheTimeAndRefreshesOnTheMinuteBoundary()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        using var vm = Create(new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time24 }, clock);

        Assert.Equal("12:00", vm.DisplayText);
        Assert.False(vm.RequiresPerSecond);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal("12:00", vm.DisplayText);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("12:01", vm.DisplayText);
    }

    [Fact]
    public void Time12Preset_AmPmDesignatorDoesNotForcePerSecond()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        using var vm = Create(new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time12 }, clock);

        Assert.Equal("12:00 PM", vm.DisplayText);
        Assert.False(vm.RequiresPerSecond);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal("12:00 PM", vm.DisplayText);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("12:01 PM", vm.DisplayText);
    }

    [Fact]
    public void CustomSecondsFormat_RefreshesEverySecond()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        using var vm = Create(
            new ClockWidgetConfig { Id = "c", Format = "HH:mm:ss" },
            clock);

        Assert.Equal("12:00:00", vm.DisplayText);
        Assert.True(vm.RequiresPerSecond);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("12:00:01", vm.DisplayText);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("12:00:02", vm.DisplayText);
    }

    [Fact]
    public void InvalidCustomFormat_IsIgnoredWithAWarningAndThePresetIsUsed()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        var (logger, sink) = TestLoggerFactory.Create();

        using var vm = Create(
            new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time24, Format = "%" },
            clock,
            logger: logger);

        Assert.Equal("HH:mm", vm.EffectiveFormat);
        Assert.Equal("12:00", vm.DisplayText);
        Assert.Single(sink.Events);
    }

    [Fact]
    public void OverLengthCustomFormat_IsIgnoredWithAWarningAndThePresetIsUsed()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        var (logger, sink) = TestLoggerFactory.Create();

        using var vm = Create(
            new ClockWidgetConfig { Id = "c", Format = new string('H', ClockWidgetConfig.MaxFormatLength + 1) },
            clock,
            logger: logger);

        Assert.Equal("HH:mm", vm.EffectiveFormat);
        Assert.Single(sink.Events);
    }

    [Fact]
    public void QuotedLiteralSeconds_DoNotForcePerSecond()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        using var vm = Create(new ClockWidgetConfig { Id = "c", Format = "HH:mm 'seconds'" }, clock);

        Assert.False(vm.RequiresPerSecond);
        Assert.Equal("12:00 seconds", vm.DisplayText);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal("12:00 seconds", vm.DisplayText);
    }

    [Fact]
    public void DisplayCulture_IsPinnedAtConstruction()
    {
        var clock = new FakeTimeProvider { UtcNow = new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero) };
        var culture = CultureInfo.GetCultureInfo("de-DE");

        using var vm = Create(new ClockWidgetConfig { Id = "c", Format = "MMMM" }, clock, culture: culture);

        Assert.Equal("Januar", vm.DisplayText);
    }

    [Fact]
    public void ClockStateEvent_RecomputesTheNextBoundaryAndTheText()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        var state = new FakeClockStateSource();
        using var vm = Create(new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time24 }, clock, state);

        Assert.Equal("12:00", vm.DisplayText);

        // The wall clock jumped (time-zone change / resume) while the timer was mid-boundary.
        clock.UtcNow = Noon.AddMinutes(5);
        state.Raise();

        Assert.Equal("12:05", vm.DisplayText);
        Assert.Equal(1, state.RaiseCount);
        Assert.True(clock.HasScheduledTimer);
    }

    [Fact]
    public void UnchangedText_RaisesNoDisplayTextNotification()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        using var vm = Create(new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time24 }, clock);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        // Two sub-boundary advances do not change the displayed minute.
        clock.Advance(TimeSpan.FromSeconds(20));
        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.DoesNotContain(nameof(ClockWidgetViewModel.DisplayText), changed);
    }

    [Fact]
    public void ApplyConfig_RefreshesModeFormatAndTextWithoutARebuild()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        using var vm = Create(new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time24 }, clock);

        vm.ApplyConfig(new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time12 });

        Assert.Equal("h:mm tt", vm.EffectiveFormat);
        Assert.Equal("12:00 PM", vm.DisplayText);
        Assert.False(vm.RequiresPerSecond);
    }

    [Fact]
    public void TimerCreationFailure_DegradesToTheInitialTextWithoutThrowing()
    {
        using var vm = new ClockWidgetViewModel(
            new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time24 },
            clockStateSource: null,
            timeProvider: new ThrowingTimeProvider(),
            culture: CultureInfo.InvariantCulture,
            timeZone: TimeZoneInfo.Utc);

        Assert.Equal("12:00", vm.DisplayText);
        Assert.False(string.IsNullOrEmpty(vm.EffectiveFormat));
    }

    [Fact]
    public void Dispose_UnsubscribesFromTheClockStateSource()
    {
        var clock = new FakeTimeProvider { UtcNow = Noon };
        var state = new FakeClockStateSource();
        var vm = Create(new ClockWidgetConfig { Id = "c" }, clock, state);

        vm.Dispose();

        clock.UtcNow = Noon.AddHours(1);
        state.Raise();

        Assert.Equal("12:00", vm.DisplayText);
    }

    private static ClockWidgetViewModel Create(
        ClockWidgetConfig config,
        FakeTimeProvider clock,
        FakeClockStateSource? state = null,
        Serilog.ILogger? logger = null,
        CultureInfo? culture = null) =>
        new(
            config,
            state,
            clock,
            dispatch: null,
            logger,
            culture: culture ?? CultureInfo.InvariantCulture,
            timeZone: TimeZoneInfo.Utc);

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Noon;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new InvalidOperationException("timers unavailable");
    }
}
