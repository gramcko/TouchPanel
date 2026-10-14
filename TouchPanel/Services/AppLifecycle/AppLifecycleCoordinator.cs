using Serilog;

namespace TouchPanel.Services.AppLifecycle;

/// <summary>How one participant invocation ended during shutdown.</summary>
public enum ShutdownParticipantStatus
{
    /// <summary>The participant completed before the shared shutdown budget elapsed.</summary>
    Completed,

    /// <summary>The participant threw; the failure was isolated and logged (RF-012, RNF-013).</summary>
    Failed,

    /// <summary>The participant did not finish before the shared shutdown budget elapsed.</summary>
    TimedOut,

    /// <summary>The budget was already spent; the participant was invoked but its completion is not awaited.</summary>
    InvokedWithoutWaiting,
}

/// <summary>Result of one participant invocation during shutdown.</summary>
/// <param name="Name">The participant name.</param>
/// <param name="Status">How the invocation ended.</param>
/// <param name="Duration">Time the coordinator spent waiting for the participant.</param>
/// <param name="Failure">The failure when the participant threw; otherwise <see langword="null"/>.</param>
public sealed record ShutdownParticipantOutcome(
    string Name,
    ShutdownParticipantStatus Status,
    TimeSpan Duration,
    Exception? Failure);

/// <summary>Summary of one shutdown run (RF-007).</summary>
/// <param name="Participants">Per-participant outcomes in invocation order.</param>
/// <param name="Duration">Total wall-clock duration of the shutdown run.</param>
public sealed record ShutdownReport(
    IReadOnlyList<ShutdownParticipantOutcome> Participants,
    TimeSpan Duration)
{
    /// <summary>Gets a value indicating whether every participant completed within the budget.</summary>
    public bool AllCompleted => Participants.All(static outcome => outcome.Status == ShutdownParticipantStatus.Completed);
}

/// <summary>
/// Owns the ordered application shutdown (RF-007, plan section 1.2). Participants are invoked
/// exactly once in registration order within one shared wall-clock budget (default ~3 s); an
/// exception or timeout in one participant is logged and does not prevent the remaining
/// participants from running (RF-012/RNF-013). Timing goes through the injected BCL
/// <see cref="TimeProvider"/> so the budget and durations stay testable (DD-14). This class
/// contains no WPF types.
/// </summary>
public sealed class AppLifecycleCoordinator
{
    /// <summary>The RF-007 default shutdown budget: subsystems must stop within ~3 seconds.</summary>
    public static readonly TimeSpan DefaultShutdownBudget = TimeSpan.FromSeconds(3);

    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly List<IApplicationShutdownParticipant> _participants = new();
    private Task<ShutdownReport>? _shutdownTask;
    private bool _shutdownStarted;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppLifecycleCoordinator"/> class.
    /// </summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="shutdownBudget">The total budget shared by all participants; defaults to <see cref="DefaultShutdownBudget"/>.</param>
    /// <param name="timeProvider">Clock used for the budget and durations; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="shutdownBudget"/> is not positive.</exception>
    public AppLifecycleCoordinator(ILogger logger, TimeSpan? shutdownBudget = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        ShutdownBudget = shutdownBudget ?? DefaultShutdownBudget;
        if (ShutdownBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shutdownBudget), ShutdownBudget, "The shutdown budget must be positive.");
        }

        _logger = logger.ForContext<AppLifecycleCoordinator>();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the total shutdown budget shared by all participants (RF-007).</summary>
    public TimeSpan ShutdownBudget { get; }

    /// <summary>
    /// Registers a participant. Participants run in registration order, so register subsystems in
    /// reverse dependency order (dependents before their dependencies).
    /// </summary>
    /// <param name="participant">The participant to register; names must be unique.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="participant"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the participant name is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the name is already registered or shutdown already started.</exception>
    public void Register(IApplicationShutdownParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (string.IsNullOrWhiteSpace(participant.Name))
        {
            throw new ArgumentException("A shutdown participant must have a non-empty name.", nameof(participant));
        }

        lock (_gate)
        {
            if (_shutdownStarted)
            {
                throw new InvalidOperationException(
                    "Shutdown participants cannot be registered after shutdown has started.");
            }

            foreach (var existing in _participants)
            {
                if (string.Equals(existing.Name, participant.Name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"A shutdown participant named '{participant.Name}' is already registered.");
                }
            }

            _participants.Add(participant);
        }

        _logger.Debug("Lifecycle: registered shutdown participant {Participant}", participant.Name);
    }

    /// <summary>
    /// Runs all registered participants once, in registration order, within
    /// <see cref="ShutdownBudget"/>. Failures and timeouts are logged and isolated, so the
    /// returned task never faults. Repeated calls return the outcome of the first run and never
    /// invoke participants twice.
    /// </summary>
    /// <param name="cancellationToken">Optionally cancels the run early; the budget still applies.</param>
    /// <returns>The shutdown report.</returns>
    public Task<ShutdownReport> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_shutdownTask is not null)
            {
                return _shutdownTask;
            }

            _shutdownStarted = true;
            _shutdownTask = ShutdownCoreAsync(cancellationToken);
            return _shutdownTask;
        }
    }

    private async Task<ShutdownReport> ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        IApplicationShutdownParticipant[] participants;
        lock (_gate)
        {
            participants = _participants.ToArray();
        }

        _logger.Information(
            "Lifecycle: shutdown started. Participants={ParticipantCount} BudgetMs={BudgetMs}",
            participants.Length,
            ShutdownBudget.TotalMilliseconds);

        var startedAt = _timeProvider.GetUtcNow();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var budgetTimer = _timeProvider.CreateTimer(
            static state => ((CancellationTokenSource)state!).Cancel(),
            deadline,
            ShutdownBudget,
            Timeout.InfiniteTimeSpan);
        var deadlineToken = deadline.Token;

        var outcomes = new List<ShutdownParticipantOutcome>(participants.Length);
        foreach (var participant in participants)
        {
            outcomes.Add(await RunParticipantAsync(participant, deadlineToken).ConfigureAwait(false));
        }

        var report = new ShutdownReport(outcomes, _timeProvider.GetUtcNow() - startedAt);

        if (report.AllCompleted)
        {
            _logger.Information(
                "Lifecycle: shutdown completed. Participants={ParticipantCount} DurationMs={DurationMs:0}",
                outcomes.Count,
                report.Duration.TotalMilliseconds);
        }
        else
        {
            _logger.Warning(
                "Lifecycle: shutdown completed with problems. Outcomes={Outcomes} DurationMs={DurationMs:0}",
                string.Join(", ", outcomes.Select(static outcome => $"{outcome.Name}:{outcome.Status}")),
                report.Duration.TotalMilliseconds);
        }

        return report;
    }

    private async Task<ShutdownParticipantOutcome> RunParticipantAsync(
        IApplicationShutdownParticipant participant,
        CancellationToken deadlineToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        ValueTask operation;
        try
        {
            operation = participant.ShutdownAsync(deadlineToken);
        }
        catch (Exception ex)
        {
            return Fail(participant, startedAt, ex);
        }

        if (operation.IsCompletedSuccessfully)
        {
            return Completed(participant, startedAt);
        }

        // Consume the ValueTask exactly once; all later observation goes through this Task.
        var task = operation.AsTask();

        if (deadlineToken.IsCancellationRequested)
        {
            // The budget is gone; the participant still gets invoked so nothing is silently
            // skipped, but waiting for it could exceed the budget without bound.
            Observe(participant, task);
            _logger.Warning(
                "Lifecycle: shutdown budget already spent; participant {Participant} was invoked without waiting",
                participant.Name);
            return new ShutdownParticipantOutcome(
                participant.Name,
                ShutdownParticipantStatus.InvokedWithoutWaiting,
                _timeProvider.GetUtcNow() - startedAt,
                Failure: null);
        }

        try
        {
            await task.WaitAsync(deadlineToken).ConfigureAwait(false);
            return Completed(participant, startedAt);
        }
        catch (OperationCanceledException) when (deadlineToken.IsCancellationRequested)
        {
            Observe(participant, task);
            _logger.Warning(
                "Lifecycle: shutdown participant {Participant} did not finish within the budget",
                participant.Name);
            return new ShutdownParticipantOutcome(
                participant.Name,
                ShutdownParticipantStatus.TimedOut,
                _timeProvider.GetUtcNow() - startedAt,
                Failure: null);
        }
        catch (Exception ex)
        {
            return Fail(participant, startedAt, ex);
        }
    }

    private ShutdownParticipantOutcome Completed(IApplicationShutdownParticipant participant, DateTimeOffset startedAt) =>
        new(participant.Name, ShutdownParticipantStatus.Completed, _timeProvider.GetUtcNow() - startedAt, Failure: null);

    private ShutdownParticipantOutcome Fail(
        IApplicationShutdownParticipant participant,
        DateTimeOffset startedAt,
        Exception failure)
    {
        _logger.Error(failure, "Lifecycle: shutdown participant {Participant} failed", participant.Name);
        return new ShutdownParticipantOutcome(
            participant.Name,
            ShutdownParticipantStatus.Failed,
            _timeProvider.GetUtcNow() - startedAt,
            failure);
    }

    /// <summary>
    /// Guards an abandoned participant task: the coordinator does not wait for it, but its late
    /// fault must still be observed and logged (RF-012) instead of surfacing as an unobserved
    /// task exception during termination.
    /// </summary>
    private void Observe(IApplicationShutdownParticipant participant, Task task)
    {
        if (task.IsCompleted)
        {
            LogObservedFailure(participant, task.Exception);
            return;
        }

        _ = task.ContinueWith(
            completed => LogObservedFailure(participant, completed.Exception),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void LogObservedFailure(IApplicationShutdownParticipant participant, AggregateException? failure)
    {
        if (failure is not null)
        {
            _logger.Warning(
                failure,
                "Lifecycle: shutdown participant {Participant} failed after the budget elapsed",
                participant.Name);
        }
    }
}
