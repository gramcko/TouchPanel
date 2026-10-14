using Serilog.Events;
using TouchPanel.Monitoring;
using TouchPanel.Services.Health;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Throttled monitoring backend re-initialization (T-05, RF-070/M2-RF-701..704): a failed backend
/// is retried at most once per retry window via the injected <see cref="TimeProvider"/>, repeated
/// failures stay observable without log spam, a successful retry restores the <c>monitoring</c>
/// subsystem and resumes normal snapshots, and a failed/throwing retry never takes the poll loop
/// down. No hardware, driver, or elevation is involved (RNF-006).
/// </summary>
public sealed class BackendReinitTests
{
    private static readonly SensorSnapshot Sample = new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        CpuTotalPercent = 12.5,
    };

    [Fact]
    public void DefaultRetryInterval_IsFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), BackendReinitializer.DefaultRetryInterval);
    }

    [Fact]
    public void TryRecover_IsGatedByTheInjectedClock()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var attempts = 0;
        var reinitializer = new BackendReinitializer(
            () =>
            {
                attempts++;
                return false;
            },
            logger,
            clock,
            TimeSpan.FromMinutes(5));

        // A brand-new backend is not retried until a full cadence has elapsed.
        Assert.False(reinitializer.TryRecover());
        Assert.Equal(0, attempts);

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(5));
        Assert.False(reinitializer.TryRecover());
        Assert.Equal(1, attempts);

        // Same instant: still throttled, never a busy retry.
        Assert.False(reinitializer.TryRecover());
        Assert.Equal(1, attempts);

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(5));
        Assert.False(reinitializer.TryRecover());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void OnBackendHealthy_ReAnchorsTheRetryWindow()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var attempts = 0;
        var reinitializer = new BackendReinitializer(
            () =>
            {
                attempts++;
                return false;
            },
            logger,
            clock,
            TimeSpan.FromMinutes(5));

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(5));
        Assert.False(reinitializer.TryRecover());
        Assert.Equal(1, attempts);

        // A healthy poll at t=5 moves the window forward: the next attempt is due at t=10.
        reinitializer.OnBackendHealthy();
        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(4));
        Assert.False(reinitializer.TryRecover());
        Assert.Equal(1, attempts);

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(1));
        Assert.False(reinitializer.TryRecover());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void FailedBackend_IsNotRetriedBeforeTheRetryWindow()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var attempts = 0;
        var source = new FakeSensorSource { LastFailureReason = "LibreHardwareMonitor initialization failed" };
        var reinitializer = new BackendReinitializer(
            () =>
            {
                attempts++;
                return false;
            },
            logger,
            clock);
        using var service = new MonitoringService(
            source, clock, logger: logger, backendReinitializer: reinitializer);

        service.PublishOnce();
        Assert.Equal(0, attempts);

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(4));
        service.PublishOnce();
        Assert.Equal(0, attempts);

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(1));
        service.PublishOnce();
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Retry_HappensAtMostOncePerWindow_RegardlessOfPollRate()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var attempts = 0;
        var source = new FakeSensorSource { LastFailureReason = "reading LibreHardwareMonitor sensors failed" };
        var reinitializer = new BackendReinitializer(
            () =>
            {
                attempts++;
                return false;
            },
            logger,
            clock);
        using var service = new MonitoringService(
            source, clock, logger: logger, backendReinitializer: reinitializer);

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(5));
        service.PublishOnce();
        service.PublishOnce();
        service.PublishOnce();
        Assert.Equal(1, attempts);

        // The loop keeps polling every cycle even while retries are throttled (no starvation).
        Assert.Equal(3, source.PollCount);

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(5));
        service.PublishOnce();
        Assert.Equal(2, attempts);
        Assert.Equal(4, source.PollCount);
    }

    [Fact]
    public void SuccessfulRetry_RestoresHealthAndResumesSnapshots()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var health = new SubsystemHealth(logger);
        var source = new FakeSensorSource { LastFailureReason = "LibreHardwareMonitor initialization failed" };
        var reinitSucceeds = false;
        var reinitializer = new BackendReinitializer(
            () =>
            {
                if (!reinitSucceeds)
                {
                    return false;
                }

                // Simulate the adapter clearing its failure and becoming readable again.
                source.LastFailureReason = null;
                source.SnapshotFactory = _ => Sample;
                return true;
            },
            logger,
            clock);
        using var service = new MonitoringService(
            source, clock, logger: logger, health: health, backendReinitializer: reinitializer);

        service.PublishOnce();
        Assert.True(health.IsDegraded("monitoring"));
        Assert.Null(service.Latest!.CpuTotalPercent);

        reinitSucceeds = true;
        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(5));
        service.PublishOnce();

        Assert.False(health.IsDegraded("monitoring"));
        Assert.Contains(
            sink.Events,
            entry => entry.RenderMessage().Contains("restored", StringComparison.Ordinal));

        // Normal snapshots resume: the recovery cycle captured before the re-open, the next one
        // reads the recovered backend.
        service.PublishOnce();
        Assert.Equal(12.5, service.Latest!.CpuTotalPercent);
    }

    [Fact]
    public void RepeatedFailedRetries_AreLoggedAtMostOncePerThrottleWindow()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var attempts = 0;
        var source = new FakeSensorSource { LastFailureReason = "LibreHardwareMonitor initialization failed" };
        var reinitializer = new BackendReinitializer(
            () =>
            {
                attempts++;
                return false;
            },
            logger,
            clock,
            retryInterval: TimeSpan.FromSeconds(10),
            failureLogThrottle: TimeSpan.FromMinutes(5));
        using var service = new MonitoringService(
            source, clock, logger: logger, backendReinitializer: reinitializer);

        for (var i = 0; i < 20; i++)
        {
            clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromSeconds(10));
            service.PublishOnce();
        }

        Assert.Equal(20, attempts);

        var warnings = sink.Events.Count(entry =>
            entry.Level == LogEventLevel.Warning
            && entry.RenderMessage().Contains("re-initialization", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, warnings);
    }

    [Fact]
    public void ThrowingReinitialization_IsContainedAndLogged()
    {
        var clock = new FakeTimeProvider();
        var (logger, sink) = TestLoggerFactory.Create();
        var source = new FakeSensorSource { LastFailureReason = "LibreHardwareMonitor initialization failed" };
        var reinitializer = new BackendReinitializer(
            () => throw new InvalidOperationException("reopen boom"),
            logger,
            clock);
        using var service = new MonitoringService(
            source, clock, logger: logger, backendReinitializer: reinitializer);

        service.PublishOnce();
        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(5));
        service.PublishOnce();

        Assert.Contains(
            sink.Events,
            entry => entry.Level == LogEventLevel.Warning
                && entry.RenderMessage().Contains("re-initializ", StringComparison.OrdinalIgnoreCase));

        // The loop survived the defective re-open and kept polling.
        Assert.Equal(2, source.PollCount);
    }

    [Fact]
    public void WithoutAReinitializer_BackendFailuresRemainContained()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        var health = new SubsystemHealth(logger);
        var source = new FakeSensorSource { LastFailureReason = "LibreHardwareMonitor initialization failed" };
        using var service = new MonitoringService(source, clock, logger: logger, health: health);

        service.PublishOnce();

        Assert.True(health.IsDegraded("monitoring"));
        Assert.Null(service.Latest!.CpuTotalPercent);
    }
}
