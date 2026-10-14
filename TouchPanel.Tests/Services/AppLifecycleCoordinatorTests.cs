using System.Diagnostics;
using Serilog;
using Serilog.Events;
using TouchPanel.Services.AppLifecycle;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

/// <summary>
/// RF-007 ordered shutdown coordinator: registration order, exactly-once invocation,
/// exception/timeout isolation, budget and cancellation enforcement, and registration semantics.
/// </summary>
public sealed class AppLifecycleCoordinatorTests
{
    [Fact]
    public async Task ShutdownAsync_InvokesParticipantsOnceInRegistrationOrder()
    {
        var order = new List<string>();
        var (coordinator, _) = CreateCoordinator();
        var first = new RecordingParticipant("first", order);
        var second = new RecordingParticipant("second", order);
        var third = new RecordingParticipant("third", order);
        coordinator.Register(first);
        coordinator.Register(second);
        coordinator.Register(third);

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(new[] { "first", "second", "third" }, order);
        Assert.Equal(1, first.Invocations);
        Assert.Equal(1, second.Invocations);
        Assert.Equal(1, third.Invocations);
        Assert.True(report.AllCompleted);
        Assert.Equal(3, report.Participants.Count);
    }

    [Fact]
    public async Task ShutdownAsync_WhenParticipantThrowsSynchronously_IsolatesFailureAndContinues()
    {
        var order = new List<string>();
        var failure = new InvalidOperationException("shutdown bug");
        var (coordinator, sink) = CreateCoordinator();
        var first = new RecordingParticipant("first", order);
        var broken = new RecordingParticipant("broken", order)
        {
            Behavior = _ => throw failure,
        };
        var last = new RecordingParticipant("last", order);
        coordinator.Register(first);
        coordinator.Register(broken);
        coordinator.Register(last);

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(new[] { "first", "broken", "last" }, order);
        var outcome = Assert.Single(report.Participants, result => result.Name == "broken");
        Assert.Equal(ShutdownParticipantStatus.Failed, outcome.Status);
        Assert.Same(failure, outcome.Failure);
        Assert.Equal(ShutdownParticipantStatus.Completed, report.Participants.Single(result => result.Name == "last").Status);
        Assert.False(report.AllCompleted);
        var logged = Assert.Single(
            sink.Events,
            logEvent => logEvent.Level == LogEventLevel.Error && ReferenceEquals(logEvent.Exception, failure));
        Assert.Contains("broken", logged.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShutdownAsync_WhenParticipantFaultsAsynchronously_IsolatesFailureAndContinues()
    {
        var order = new List<string>();
        var failure = new TimeoutException("async failure");
        var (coordinator, sink) = CreateCoordinator();
        var broken = new RecordingParticipant("broken", order)
        {
            Behavior = _ => ValueTask.FromException(failure),
        };
        var last = new RecordingParticipant("last", order);
        coordinator.Register(broken);
        coordinator.Register(last);

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(new[] { "broken", "last" }, order);
        Assert.Equal(ShutdownParticipantStatus.Failed, report.Participants[0].Status);
        Assert.Same(failure, report.Participants[0].Failure);
        Assert.Equal(ShutdownParticipantStatus.Completed, report.Participants[1].Status);
        Assert.Contains(
            sink.Events,
            logEvent => logEvent.Level == LogEventLevel.Error && ReferenceEquals(logEvent.Exception, failure));
    }

    [Fact]
    public async Task ShutdownAsync_WhenParticipantIgnoresCancellation_RespectsBudgetAndContinues()
    {
        var order = new List<string>();
        var (coordinator, sink) = CreateCoordinator(TimeSpan.FromMilliseconds(150));
        var hanging = new RecordingParticipant("hanging", order)
        {
            Behavior = _ => new ValueTask(NeverCompletes()),
        };
        var alsoHanging = new RecordingParticipant("also-hanging", order)
        {
            Behavior = _ => new ValueTask(NeverCompletes()),
        };
        var quick = new RecordingParticipant("quick", order);
        coordinator.Register(hanging);
        coordinator.Register(alsoHanging);
        coordinator.Register(quick);

        var stopwatch = Stopwatch.StartNew();
        var report = await coordinator.ShutdownAsync();
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Shutdown took {stopwatch.Elapsed}.");
        Assert.Equal(new[] { "hanging", "also-hanging", "quick" }, order);
        Assert.Equal(1, hanging.Invocations);
        Assert.Equal(1, alsoHanging.Invocations);
        Assert.Equal(1, quick.Invocations);
        Assert.Equal(ShutdownParticipantStatus.TimedOut, report.Participants[0].Status);
        Assert.Equal(ShutdownParticipantStatus.InvokedWithoutWaiting, report.Participants[1].Status);
        Assert.Equal(ShutdownParticipantStatus.Completed, report.Participants[2].Status);
        Assert.False(report.AllCompleted);
        Assert.Contains(
            sink.Events,
            logEvent => logEvent.Level == LogEventLevel.Warning
                && logEvent.RenderMessage().Contains("hanging", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShutdownAsync_WhenParticipantHonorsCancellation_TimesOutOnBudget()
    {
        var order = new List<string>();
        var (coordinator, _) = CreateCoordinator(TimeSpan.FromMilliseconds(150));
        var waiting = new RecordingParticipant("waiting", order)
        {
            Behavior = token => new ValueTask(Task.Delay(Timeout.Infinite, token)),
        };
        coordinator.Register(waiting);

        var stopwatch = Stopwatch.StartNew();
        var report = await coordinator.ShutdownAsync();
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Shutdown took {stopwatch.Elapsed}.");
        Assert.Equal(1, waiting.Invocations);
        Assert.Equal(ShutdownParticipantStatus.TimedOut, report.Participants[0].Status);
    }

    [Fact]
    public async Task ShutdownAsync_WhenCallerCancels_StopsEarlyAndContinuesThroughRemainingParticipants()
    {
        var order = new List<string>();
        var (coordinator, _) = CreateCoordinator(TimeSpan.FromSeconds(30));
        var hanging = new RecordingParticipant("hanging", order)
        {
            Behavior = _ => new ValueTask(NeverCompletes()),
        };
        var later = new RecordingParticipant("later", order);
        coordinator.Register(hanging);
        coordinator.Register(later);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var stopwatch = Stopwatch.StartNew();
        var report = await coordinator.ShutdownAsync(cancellation.Token);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Shutdown took {stopwatch.Elapsed}.");
        Assert.Equal(new[] { "hanging", "later" }, order);
        Assert.Equal(ShutdownParticipantStatus.TimedOut, report.Participants[0].Status);
        Assert.Equal(ShutdownParticipantStatus.Completed, report.Participants[1].Status);
    }

    [Fact]
    public async Task ShutdownAsync_CalledTwice_ReturnsSameRunAndInvokesParticipantsOnce()
    {
        var order = new List<string>();
        var (coordinator, _) = CreateCoordinator();
        var participant = new RecordingParticipant("once", order);
        coordinator.Register(participant);

        var first = coordinator.ShutdownAsync();
        var second = coordinator.ShutdownAsync();

        Assert.Same(first, second);
        await first;
        Assert.Equal(1, participant.Invocations);
    }

    [Fact]
    public async Task ShutdownAsync_WithDelegateParticipant_InvokesTheDelegateOnce()
    {
        var (coordinator, _) = CreateCoordinator();
        var invocations = 0;
        coordinator.Register(new DelegateApplicationShutdownParticipant("delegate", token =>
        {
            Assert.False(token.IsCancellationRequested);
            invocations++;
            return ValueTask.CompletedTask;
        }));

        var report = await coordinator.ShutdownAsync();

        Assert.Equal(1, invocations);
        Assert.True(report.AllCompleted);
    }

    [Fact]
    public async Task Register_NullEmptyDuplicateOrAfterShutdown_Throws()
    {
        var order = new List<string>();
        var (coordinator, _) = CreateCoordinator();
        coordinator.Register(new RecordingParticipant("known", order));

        Assert.Throws<ArgumentNullException>(() => coordinator.Register(null!));
        Assert.Throws<ArgumentException>(() => coordinator.Register(new RecordingParticipant(" ", order)));
        Assert.Throws<InvalidOperationException>(() => coordinator.Register(new RecordingParticipant("known", order)));

        await coordinator.ShutdownAsync();

        Assert.Throws<InvalidOperationException>(() => coordinator.Register(new RecordingParticipant("late", order)));
    }

    [Fact]
    public void Constructor_NonPositiveBudget_Throws()
    {
        var logger = new LoggerConfiguration().CreateLogger();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AppLifecycleCoordinator(logger, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AppLifecycleCoordinator(logger, TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task ShutdownAsync_WithoutParticipants_CompletesWithEmptyReport()
    {
        var (coordinator, _) = CreateCoordinator();

        var report = await coordinator.ShutdownAsync();

        Assert.Empty(report.Participants);
        Assert.True(report.AllCompleted);
    }

    private static (AppLifecycleCoordinator Coordinator, RecordingSink Sink) CreateCoordinator(TimeSpan? budget = null)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new AppLifecycleCoordinator(logger, budget), sink);
    }

    private static Task NeverCompletes() => new TaskCompletionSource<object?>().Task;

    private sealed class RecordingParticipant : IApplicationShutdownParticipant
    {
        private readonly List<string> _invocations;

        public RecordingParticipant(string name, List<string> invocations)
        {
            Name = name;
            _invocations = invocations;
        }

        public string Name { get; }

        public int Invocations { get; private set; }

        public Func<CancellationToken, ValueTask>? Behavior { get; set; }

        public ValueTask ShutdownAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            _invocations.Add(Name);
            return Behavior?.Invoke(cancellationToken) ?? ValueTask.CompletedTask;
        }
    }
}
