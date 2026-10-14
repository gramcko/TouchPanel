using Serilog.Events;
using TouchPanel.Monitoring;
using TouchPanel.Services.Health;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Monitoring service lifecycle and cadence (T-11, RF-064..RF-067): immediate first sample, exactly
/// one publication per 1 Hz interval (never faster), failure degradation to an unavailable snapshot
/// with throttled logging, per-subscriber isolation, and disposal that leaves no live timer, task,
/// or hardware source behind.
/// </summary>
public sealed class MonitoringServiceTests
{
    private static readonly SensorSnapshot Sample = new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        CpuTotalPercent = 12.5,
    };

    [Fact]
    public void DefaultInterval_IsOneHertz()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), MonitoringService.DefaultInterval);
    }

    [Fact]
    public void Latest_IsNullBeforeTheFirstPoll()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        using var service = new MonitoringService(new FakeSensorSource(), clock, logger: logger);

        Assert.Null(service.Latest);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Start_PublishesImmediatelyThenOncePerInterval_NeverFaster()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        using var service = new MonitoringService(source, clock, logger: logger);

        var received = 0;
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);

        service.Start();
        Assert.True(service.IsRunning);

        await WaitUntilAsync(() => Volatile.Read(ref received) == 1);
        await WaitUntilAsync(() => clock.HasScheduledTimer);

        // Half an interval: no timer is due, so no publication and no UI churn.
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref received));

        // Crossing the interval: exactly one more publication.
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await WaitUntilAsync(() => Volatile.Read(ref received) == 2);
        Assert.Equal(2, source.PollCount);

        service.Stop();
        Assert.False(service.IsRunning);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task StopBeforeStart_AndRepeatedStop_AreNoOps()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        using var service = new MonitoringService(new FakeSensorSource(), clock, logger: logger);

        service.Stop();
        service.Stop();

        await Task.CompletedTask;
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Start_IsIdempotent()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        using var service = new MonitoringService(source, clock, logger: logger);

        var received = 0;
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);

        service.Start();
        service.Start();

        await WaitUntilAsync(() => Volatile.Read(ref received) >= 1);
        await Task.Delay(50);

        // A second loop would have produced a second immediate poll.
        Assert.Equal(1, Volatile.Read(ref received));
    }

    [Fact]
    public async Task StartAfterStop_RestartsPolling()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        using var service = new MonitoringService(source, clock, logger: logger);

        var received = 0;
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);

        service.Start();
        await WaitUntilAsync(() => Volatile.Read(ref received) == 1);
        service.Stop();

        service.Start();
        await WaitUntilAsync(() => Volatile.Read(ref received) == 2);

        service.Stop();
        Assert.Equal(2, source.PollCount);
    }

    [Fact]
    public async Task Dispose_StopsTheLoop_DisposesTheSource_AndLeavesNoTimers()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        var service = new MonitoringService(source, clock, logger: logger);

        var received = 0;
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);

        service.Start();
        await WaitUntilAsync(() => Volatile.Read(ref received) >= 1);
        await WaitUntilAsync(() => clock.HasScheduledTimer);

        service.Dispose();

        Assert.False(service.IsRunning);
        Assert.True(source.Disposed);
        Assert.Equal(0, clock.ActiveTimerCount);

        var afterDispose = Volatile.Read(ref received);
        clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);
        Assert.Equal(afterDispose, Volatile.Read(ref received));
    }

    [Fact]
    public void StartAfterDispose_Throws()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var service = new MonitoringService(new FakeSensorSource(), clock, logger: logger);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.Start());
    }

    [Fact]
    public void PublishOnce_SourceFailure_PublishesAnExplicitlyUnavailableSnapshot_AndKeepsRunning()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { PollException = new InvalidOperationException("boom") };
        using var service = new MonitoringService(source, clock, logger: logger);

        service.PublishOnce();

        Assert.NotNull(service.Latest);
        Assert.Null(service.Latest!.CpuTotalPercent);
        Assert.Contains(
            sink.Events,
            entry => entry.Level == LogEventLevel.Warning
                && entry.RenderMessage().Contains("reading hardware sensors failed", StringComparison.Ordinal));

        // A transient failure recovers on the next successful poll.
        source.PollException = null;
        source.SnapshotFactory = _ => Sample;
        service.PublishOnce();

        Assert.Equal(12.5, service.Latest!.CpuTotalPercent);
    }

    [Fact]
    public void PublishOnce_WhenTheSourceReportsABackendFailure_MarksMonitoringDegraded()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var health = new SubsystemHealth(logger);
        var source = new FakeSensorSource { LastFailureReason = "LibreHardwareMonitor initialization failed" };
        using var service = new MonitoringService(source, clock, logger: logger, health: health);

        service.PublishOnce();

        var status = health.GetStatus("monitoring");
        Assert.NotNull(status);
        Assert.Equal(SubsystemState.Degraded, status!.State);
        Assert.Equal("LibreHardwareMonitor initialization failed", status.Reason);
    }

    [Fact]
    public void PublishOnce_RepeatedBackendFailures_MarkDegradedOnceWithoutLogSpam()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var health = new SubsystemHealth(logger);
        var source = new FakeSensorSource { LastFailureReason = "reading LibreHardwareMonitor sensors failed" };
        using var service = new MonitoringService(source, clock, logger: logger, health: health);

        for (var i = 0; i < 10; i++)
        {
            service.PublishOnce();
        }

        Assert.True(health.IsDegraded("monitoring"));
        Assert.Single(
            sink.Events,
            entry => entry.RenderMessage().Contains("degraded", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishOnce_AfterARecoveredPoll_RestoresMonitoring()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var health = new SubsystemHealth(logger);
        var source = new FakeSensorSource { LastFailureReason = "LibreHardwareMonitor initialization failed" };
        using var service = new MonitoringService(source, clock, logger: logger, health: health);
        service.PublishOnce();
        Assert.True(health.IsDegraded("monitoring"));

        source.LastFailureReason = null;
        source.SnapshotFactory = _ => Sample;
        service.PublishOnce();

        var status = health.GetStatus("monitoring");
        Assert.NotNull(status);
        Assert.Equal(SubsystemState.Healthy, status!.State);
        Assert.Equal(12.5, service.Latest!.CpuTotalPercent);
        Assert.Contains(
            sink.Events,
            entry => entry.RenderMessage().Contains("restored", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishOnce_WhenTheSourceThrows_MarksMonitoringDegraded()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var health = new SubsystemHealth(logger);
        var source = new FakeSensorSource { PollException = new InvalidOperationException("boom") };
        using var service = new MonitoringService(source, clock, logger: logger, health: health);

        service.PublishOnce();

        var status = health.GetStatus("monitoring");
        Assert.NotNull(status);
        Assert.Equal(SubsystemState.Degraded, status!.State);
        Assert.Equal("reading hardware sensors failed", status.Reason);
    }

    [Fact]
    public void PublishOnce_RepeatedFailures_AreLoggedAtMostOncePerThrottleWindow()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { PollException = new InvalidOperationException("boom") };
        using var service = new MonitoringService(
            source,
            clock,
            failureLogThrottle: TimeSpan.FromMinutes(1),
            logger: logger);

        for (var i = 0; i < 10; i++)
        {
            service.PublishOnce();
        }

        Assert.Single(sink.Events, entry => entry.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void PublishOnce_OneSubscriberThrows_OtherSubscribersStillReceive()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { SnapshotFactory = _ => Sample };
        using var service = new MonitoringService(source, clock, logger: logger);

        var received = 0;
        service.SnapshotAvailable += _ => throw new InvalidOperationException("subscriber boom");
        service.SnapshotAvailable += _ => Interlocked.Increment(ref received);

        service.PublishOnce();

        Assert.Equal(1, Volatile.Read(ref received));
        Assert.Contains(
            sink.Events,
            entry => entry.RenderMessage().Contains("snapshot subscriber failed", StringComparison.Ordinal));
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
