using System.Globalization;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Services.Time;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Services;

/// <summary>
/// Shared Clock validation/display culture (M4-RF-951/D-M4-5, AC-M4-04): the validator and the Clock
/// view model both resolve a custom format through one injected <see cref="IClockCultureSource"/>, so
/// a format accepted at load time is also accepted at runtime and vice versa. A counting source
/// proves both consume the same instance rather than each reading its own culture.
/// </summary>
public sealed class ClockCultureTests
{
    private static readonly DateTimeOffset January = new(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClockCultureSource_PinsTheSuppliedCulture()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");

        var source = new ClockCultureSource(culture);

        Assert.Same(culture, source.Culture);
    }

    [Fact]
    public void ClockCultureSource_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ClockCultureSource(null!));
    }

    [Fact]
    public void ValidatorAndViewModel_ConsumeTheSameInjectedCultureSource()
    {
        // A culture that differs from both InvariantCulture and the (typically en-US) machine default.
        var source = new CountingClockCultureSource(CultureInfo.GetCultureInfo("de-DE"));

        var validated = new ClockWidgetConfig { Id = "c", Width = 200, Height = 100, Format = "MMMM" };
        var diagnostics = ConfigValidator.Validate(ConfigWith(validated), source);

        Assert.True(source.ReadCount > 0, "the validator must resolve the supplied culture source");
        Assert.Equal("MMMM", validated.Format);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.clock.format");

        var readsAfterValidation = source.ReadCount;
        using var vm = CreateViewModel(new ClockWidgetConfig { Id = "c", Format = "MMMM" }, source);

        Assert.True(source.ReadCount > readsAfterValidation, "the view model must resolve the same culture source");
        Assert.Equal("MMMM", vm.EffectiveFormat);
        Assert.Equal("Januar", vm.DisplayText);
    }

    [Fact]
    public void FormatInvalidInThePinnedCulture_IsRejectedByBothValidatorAndViewModel()
    {
        var source = new ClockCultureSource(CultureInfo.GetCultureInfo("de-DE"));

        var validated = new ClockWidgetConfig { Id = "c", Width = 200, Height = 100, Format = "%" };
        var diagnostics = ConfigValidator.Validate(ConfigWith(validated), source);

        Assert.Null(validated.Format);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.clock.format");

        using var vm = CreateViewModel(new ClockWidgetConfig { Id = "c", Mode = ClockMode.Time24, Format = "%" }, source);
        Assert.Equal("HH:mm", vm.EffectiveFormat);
    }

    [Fact]
    public void DefaultSource_MatchesTheFallbackUsedWhenNoneIsInjected()
    {
        // The validator's no-argument overload and the view model's no-culture construction both fall
        // back to the same canonical source, so their defaults agree.
        var validated = new ClockWidgetConfig { Id = "c", Width = 200, Height = 100, Format = "HH:mm" };
        ConfigValidator.Validate(ConfigWith(validated));

        Assert.Equal("HH:mm", validated.Format);

        using var vm = CreateViewModel(new ClockWidgetConfig { Id = "c", Format = "HH:mm" }, source: null);
        Assert.True(ClockFormat.TryCustomFormat(vm.EffectiveFormat, ClockCultureSource.Default.Culture));
    }

    private static ClockWidgetViewModel CreateViewModel(ClockWidgetConfig config, IClockCultureSource? source) =>
        new(
            config,
            clockStateSource: null,
            timeProvider: new FakeTimeProvider { UtcNow = January },
            dispatch: null,
            logger: null,
            culture: null,
            timeZone: TimeZoneInfo.Utc,
            clockCulture: source);

    private static PanelConfig ConfigWith(WidgetConfig widget) =>
        new()
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets = [widget],
                },
            ],
        };

    private sealed class CountingClockCultureSource : IClockCultureSource
    {
        private readonly CultureInfo _culture;
        private int _readCount;

        public CountingClockCultureSource(CultureInfo culture) => _culture = culture;

        public CultureInfo Culture
        {
            get
            {
                Interlocked.Increment(ref _readCount);
                return _culture;
            }
        }

        public int ReadCount => _readCount;
    }
}
