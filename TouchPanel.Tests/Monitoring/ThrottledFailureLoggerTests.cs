using Serilog.Events;
using TouchPanel.Monitoring;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Failure-log throttling (T-11, RF-065): repeated poll failures must produce a bounded number of
/// log events; the first failure logs immediately, further failures inside the window are counted,
/// and the next logged failure reports how many were suppressed. The fake clock keeps the schedule
/// deterministic.
/// </summary>
public sealed class ThrottledFailureLoggerTests
{
    [Fact]
    public void Report_LogsTheFirstFailure_ThenSuppressesWithinTheWindow()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var throttle = new ThrottledFailureLogger(logger, clock, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 10; i++)
        {
            throttle.Report(new InvalidOperationException("boom"), "Poll failed");
        }

        var entry = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, entry.Level);
        Assert.Equal("Poll failed", entry.RenderMessage());
    }

    [Fact]
    public void Report_LogsAgainAfterTheWindow_AndReportsTheSuppressedCount()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var throttle = new ThrottledFailureLogger(logger, clock, TimeSpan.FromMinutes(1));

        throttle.Report(new InvalidOperationException("boom"), "Poll failed");
        for (var i = 0; i < 9; i++)
        {
            throttle.Report(new InvalidOperationException("boom"), "Poll failed");
        }

        clock.Advance(TimeSpan.FromSeconds(59));
        throttle.Report(new InvalidOperationException("boom"), "Poll failed");
        Assert.Single(sink.Events);

        clock.Advance(TimeSpan.FromSeconds(1));
        throttle.Report(new InvalidOperationException("boom"), "Poll failed");

        Assert.Equal(2, sink.Events.Count);
        var second = sink.Events[1];
        Assert.True(second.Properties.TryGetValue("SuppressedCount", out var value));
        Assert.Equal(10, Convert.ToInt32(value!.ToString().Trim('"')));
        Assert.Contains("SuppressedCount=10", second.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Report_AtExactlyTheWindowBoundary_LogsImmediately()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var throttle = new ThrottledFailureLogger(logger, clock, TimeSpan.FromMinutes(1));

        throttle.Report(null, "Failure");
        clock.Advance(TimeSpan.FromMinutes(1));
        throttle.Report(null, "Failure");

        Assert.Equal(2, sink.Events.Count);
    }

    [Fact]
    public void Report_ZeroInterval_LogsEveryFailure()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var throttle = new ThrottledFailureLogger(logger, clock, TimeSpan.Zero);

        throttle.Report(null, "Failure");
        throttle.Report(null, "Failure");

        Assert.Equal(2, sink.Events.Count);
    }

    [Fact]
    public void Constructor_NegativeInterval_Throws()
    {
        var (logger, _) = TestLoggerFactory.Create();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ThrottledFailureLogger(logger, null, TimeSpan.FromSeconds(-1)));
    }
}
