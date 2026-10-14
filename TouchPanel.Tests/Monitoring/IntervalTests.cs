using Serilog.Events;
using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Configurable polling interval and live application (T-04, RF-071/M2-RF-711..714): the effective
/// cadence matches the configured interval, a live change restarts the single loop at the new
/// cadence (never two loops, no leaked timers), the bounded history capacity is recomputed while
/// preserving the newest samples, and out-of-range values are clamped with a warning.
/// </summary>
public sealed class IntervalTests
{
    private static readonly SensorSnapshot Sample = new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        CpuTotalPercent = 12.5,
    };

    [Fact]
    public async Task EffectiveCadence_MatchesConfiguredInterval()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        using var service = new MonitoringService(
            source,
            clock,
            interval: TimeSpan.FromSeconds(2),
            logger: logger);

        var received = 0;
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);

        service.Start();

        // The first sample is immediate; no second sample before a full 2 s interval.
        await WaitUntilAsync(() => Volatile.Read(ref received) == 1);
        await WaitUntilAsync(() => clock.HasScheduledTimer);

        clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref received));

        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => Volatile.Read(ref received) == 2);
        Assert.Equal(2, source.PollCount);

        service.Stop();
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task ApplyInterval_WhileRunning_RestartsTheSingleLoopAtTheNewCadence_WithOneImmediateSample()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        using var service = new MonitoringService(
            source,
            clock,
            interval: TimeSpan.FromSeconds(1),
            logger: logger);

        var received = 0;
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);

        service.Start();
        await WaitUntilAsync(() => Volatile.Read(ref received) == 1);
        await WaitUntilAsync(() => clock.HasScheduledTimer);

        service.ApplyInterval(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), service.Interval);

        // Exactly one immediate sample from the restarted loop; no second loop producing another.
        await WaitUntilAsync(() => Volatile.Read(ref received) == 2);
        await Task.Delay(50);
        Assert.Equal(2, Volatile.Read(ref received));

        // The new cadence is 2 s: 1 s later nothing new, at 2 s exactly one more.
        clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        Assert.Equal(2, Volatile.Read(ref received));

        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => Volatile.Read(ref received) == 3);
        Assert.Equal(3, source.PollCount);

        service.Stop();
        Assert.False(service.IsRunning);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task ApplyInterval_SameInterval_DoesNotRestartOrPublishImmediately()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        using var service = new MonitoringService(
            source,
            clock,
            interval: TimeSpan.FromSeconds(1),
            logger: logger);

        var received = 0;
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);

        service.Start();
        await WaitUntilAsync(() => Volatile.Read(ref received) == 1);
        await WaitUntilAsync(() => clock.HasScheduledTimer);

        service.ApplyInterval(TimeSpan.FromSeconds(1));

        // No restart, so no extra immediate sample beyond the one the running loop produced.
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref received));
        Assert.True(service.IsRunning);

        service.Stop();
    }

    [Fact]
    public async Task ApplyInterval_WhileStopped_UpdatesIntervalAndCapacity_AndIsUsedOnNextStart()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        using var store = new SensorHistoryStore(TimeSpan.FromSeconds(1), windowSeconds: 60);
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        using var service = new MonitoringService(source, clock, logger: logger, history: store);

        Assert.Equal(60, store.Capacity);

        service.ApplyInterval(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(2), service.Interval);
        Assert.False(service.IsRunning);
        Assert.Equal(30, store.Capacity);

        var received = 0;
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);
        service.Start();
        await WaitUntilAsync(() => Volatile.Read(ref received) == 1);
        await WaitUntilAsync(() => clock.HasScheduledTimer);

        clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref received));

        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => Volatile.Read(ref received) == 2);

        service.Stop();
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public void ApplyInterval_RecomputesHistoryCapacity_PreservingTheNewestSamples()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        using var store = new SensorHistoryStore(TimeSpan.FromSeconds(1), windowSeconds: 10);
        store.Track("cpu.total");
        var source = new FakeSensorSource
        {
            SnapshotFactory = poll => new SensorSnapshot { CpuTotalPercent = poll },
        };
        using var service = new MonitoringService(
            source,
            clock,
            interval: TimeSpan.FromSeconds(1),
            logger: logger,
            history: store);

        for (var i = 0; i < 7; i++)
        {
            service.PublishOnce();
        }

        Assert.Equal(10, store.Capacity);
        Assert.Equal(new double?[] { 1, 2, 3, 4, 5, 6, 7 }, store.GetHistory("cpu.total"));

        // Shrink the interval capacity (10 s / 2 s = 5): the newest five samples are preserved.
        service.ApplyInterval(TimeSpan.FromSeconds(2));

        Assert.Equal(5, store.Capacity);
        Assert.Equal(new double?[] { 3, 4, 5, 6, 7 }, store.GetHistory("cpu.total"));
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(0.0, 0.5)]
    [InlineData(-3.0, 0.5)]
    [InlineData(10.0, 5.0)]
    [InlineData(2.5, 2.5)]
    public void ApplyInterval_OutOfRange_IsClampedWithWarning(double requestedSeconds, double expectedSeconds)
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        using var service = new MonitoringService(new FakeSensorSource(), clock, logger: logger);

        service.ApplyInterval(TimeSpan.FromSeconds(requestedSeconds));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), service.Interval);

        if (requestedSeconds != expectedSeconds)
        {
            Assert.Contains(
                sink.Events,
                entry => entry.Level == LogEventLevel.Warning
                    && entry.RenderMessage().Contains("clamped", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void MonitoringService_IntervalBounds_MatchTheConfigurationBounds()
    {
        // Guards against the defensive clamp in MonitoringService diverging from the schema bounds
        // enforced by ConfigValidator (RF-071/M2-RF-711).
        Assert.Equal(
            TimeSpan.FromSeconds(TelemetryConfig.MinPollIntervalSeconds),
            MonitoringService.MinimumInterval);
        Assert.Equal(
            TimeSpan.FromSeconds(TelemetryConfig.MaxPollIntervalSeconds),
            MonitoringService.MaximumInterval);
        Assert.Equal(
            TimeSpan.FromSeconds(TelemetryConfig.DefaultPollIntervalSeconds),
            MonitoringService.DefaultInterval);
    }

    [Fact]
    public void ApplyInterval_AfterDispose_Throws()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var service = new MonitoringService(new FakeSensorSource(), clock, logger: logger);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.ApplyInterval(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Reconfigure_ByIntervalOnly_UsesTheStoredWindow()
    {
        using var store = new SensorHistoryStore(TimeSpan.FromSeconds(1), windowSeconds: 10);
        store.Track("cpu.total");
        store.Record(new SensorSnapshot { CpuTotalPercent = 1 });

        store.Reconfigure(TimeSpan.FromSeconds(2));

        Assert.Equal(10, store.WindowSeconds);
        Assert.Equal(5, store.Capacity);
        Assert.Equal(new double?[] { 1 }, store.GetHistory("cpu.total"));
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds, bounded by <paramref name="timeoutMs"/>.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"The condition was not met within {timeoutMs} ms.");
            }

            await Task.Delay(5).ConfigureAwait(false);
        }
    }
}
