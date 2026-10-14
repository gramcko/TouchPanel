using System.Globalization;
using TouchPanel.Config;
using TouchPanel.Services.Time;

namespace TouchPanel.Tests.Services;

/// <summary>
/// Pure clock-format rules (RF-046/M3-RF-462/463): preset mapping, safe custom-format validation,
/// escaped-literal-aware seconds detection (with the `t`/`tt` designator excluded), and the
/// boundary computation that drives the refresh cadence.
/// </summary>
public sealed class ClockFormatTests
{
    [Theory]
    [InlineData(ClockMode.Time24, "HH:mm")]
    [InlineData(ClockMode.Time12, "h:mm tt")]
    [InlineData(ClockMode.DateShort, "d")]
    [InlineData(ClockMode.DateLong, "D")]
    [InlineData(ClockMode.DateTime, "g")]
    [InlineData(ClockMode.Unknown, "HH:mm")]
    public void PresetFormat_MapsEachMode(ClockMode mode, string expected) =>
        Assert.Equal(expected, ClockFormat.PresetFormat(mode));

    [Theory]
    [InlineData("HH:mm", false)]
    [InlineData("h:mm tt", false)]
    [InlineData("yyyy-MM-dd", false)]
    [InlineData("HH:mm:ss", true)]
    [InlineData("HH:mm:ss.fff", true)]
    [InlineData("HH:mm:ss.FFF", true)]
    [InlineData("HH:mm 'seconds'", false)]
    [InlineData("HH:mm \"seconds\"", false)]
    [InlineData(@"HH:mm \s", false)]
    [InlineData(@"HH\:mm\:ss", true)]
    public void RequiresPerSecondRefresh_IsEscapedLiteralAware(string format, bool expected) =>
        Assert.Equal(expected, ClockFormat.RequiresPerSecondRefresh(format));

    [Fact]
    public void RequiresPerSecondRefresh_AmPmDesignatorDoesNotForcePerSecond() =>
        Assert.False(ClockFormat.RequiresPerSecondRefresh("h:mm tt"));

    [Theory]
    [InlineData("HH:mm:ss", true)]
    [InlineData("h:mm tt", true)]
    [InlineData("%", false)]
    [InlineData("HH\\", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void TryCustomFormat_ValidatesLengthAndSafety(string format, bool expected) =>
        Assert.Equal(expected, ClockFormat.TryCustomFormat(format, CultureInfo.InvariantCulture));

    [Fact]
    public void TryCustomFormat_RejectsOverLength()
    {
        var overLength = new string('H', ClockWidgetConfig.MaxFormatLength + 1);

        Assert.False(ClockFormat.TryCustomFormat(overLength, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0, 0, true, 1000)]
    [InlineData(0, 250, true, 750)]
    [InlineData(0, 0, false, 60000)]
    [InlineData(30, 500, false, 29500)]
    public void ComputeNextBoundary_ReturnsTheNextWholeBoundary(
        int second,
        int millisecond,
        bool perSecond,
        double expectedMilliseconds)
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, second, millisecond, TimeSpan.Zero);

        var boundary = ClockFormat.ComputeNextBoundary(now, perSecond);

        Assert.Equal(expectedMilliseconds, boundary.TotalMilliseconds);
        Assert.True(boundary > TimeSpan.Zero);
    }
}
