using Serilog;
using Serilog.Events;
using TouchPanel.Services.Health;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

public class SubsystemHealthTests
{
    private static (SubsystemHealth Health, RecordingSink Sink) Create(TimeProvider? timeProvider = null)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new SubsystemHealth(logger, timeProvider), sink);
    }

    [Fact]
    public void Register_NewSubsystem_ReturnsTrueAndReportsHealthy()
    {
        var (health, _) = Create();

        Assert.True(health.Register("telemetry"));

        var status = health.GetStatus("telemetry");
        Assert.NotNull(status);
        Assert.Equal(SubsystemState.Healthy, status.State);
        Assert.Null(status.Reason);
        Assert.False(health.IsDegraded("telemetry"));
    }

    [Fact]
    public void Register_AlreadyRegistered_ReturnsFalse()
    {
        var (health, _) = Create();

        Assert.True(health.Register("telemetry"));
        Assert.False(health.Register("telemetry"));
    }

    [Fact]
    public void MarkDegraded_TransitionsToDegraded_AndLogsWarningOnce()
    {
        var (health, sink) = Create();
        health.Register("telemetry");
        var failure = new InvalidOperationException("sensor read failed");

        Assert.True(health.MarkDegraded("telemetry", "sensor read failed", failure));
        Assert.True(health.IsDegraded("telemetry"));
        Assert.Equal("sensor read failed", health.GetStatus("telemetry")!.Reason);

        // Repeating the same degradation is a no-op and must not spam the log (RNF-007).
        Assert.False(health.MarkDegraded("telemetry", "sensor read failed again"));

        var logEvent = Assert.Single(sink.Events, e => e.Level == LogEventLevel.Warning);
        Assert.Equal(LogEventLevel.Warning, logEvent.Level);
        Assert.Same(failure, logEvent.Exception);
        var message = logEvent.RenderMessage();
        Assert.Contains("telemetry", message, StringComparison.Ordinal);
        Assert.Contains("sensor read failed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkDegraded_UnregisteredSubsystem_AutoRegisters()
    {
        var (health, _) = Create();

        Assert.True(health.MarkDegraded("hotkeys", "registration failed"));

        Assert.True(health.IsDegraded("hotkeys"));
    }

    [Fact]
    public void MarkRestored_TransitionsBackToHealthy_AndLogsInformationOnce()
    {
        var (health, sink) = Create();
        health.Register("telemetry");
        health.MarkDegraded("telemetry", "sensor read failed");
        var eventsBeforeRestore = sink.Events.Count;

        Assert.True(health.MarkRestored("telemetry", "sensor recovered"));
        Assert.False(health.MarkRestored("telemetry"));

        var status = health.GetStatus("telemetry");
        Assert.NotNull(status);
        Assert.Equal(SubsystemState.Healthy, status.State);
        Assert.Equal("sensor recovered", status.Reason);

        var transition = Assert.Single(sink.Events.Skip(eventsBeforeRestore));
        Assert.Equal(LogEventLevel.Information, transition.Level);
        Assert.Contains("telemetry", transition.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void MarkRestored_WhenNeverRegistered_AutoRegistersHealthy()
    {
        var (health, _) = Create();

        Assert.True(health.MarkRestored("config"));
        Assert.False(health.IsDegraded("config"));
    }

    [Fact]
    public void GetStatus_UnknownSubsystem_ReturnsNull()
    {
        var (health, _) = Create();

        Assert.Null(health.GetStatus("unknown"));
        Assert.False(health.IsDegraded("unknown"));
    }

    [Fact]
    public void Transitions_TimestampWithInjectedTimeProvider()
    {
        var time = new FakeTimeProvider();
        var (health, _) = Create(time);
        var registeredAt = time.UtcNow;

        health.Register("telemetry");
        Assert.Equal(registeredAt, health.GetStatus("telemetry")!.Since);

        time.UtcNow = registeredAt.AddMinutes(5);
        health.MarkDegraded("telemetry", "sensor read failed");
        Assert.Equal(time.UtcNow, health.GetStatus("telemetry")!.Since);

        time.UtcNow = registeredAt.AddMinutes(10);
        health.MarkRestored("telemetry");
        Assert.Equal(time.UtcNow, health.GetStatus("telemetry")!.Since);
    }

    [Fact]
    public void GetAllStatuses_ReturnsEverySubsystemOrderedByName()
    {
        var (health, _) = Create();
        health.Register("telemetry");
        health.Register("actions");
        health.Register("config");

        var statuses = health.GetAllStatuses();

        Assert.Equal(new[] { "actions", "config", "telemetry" }, statuses.Select(s => s.Name));
    }
}
