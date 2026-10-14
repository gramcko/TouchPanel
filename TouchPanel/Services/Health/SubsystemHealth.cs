using Serilog;

namespace TouchPanel.Services.Health;

/// <summary>Health state of an isolated subsystem.</summary>
public enum SubsystemState
{
    /// <summary>The subsystem is operating normally.</summary>
    Healthy,

    /// <summary>The subsystem failed and is isolated; the rest of the application continues (RF-012).</summary>
    Degraded,
}

/// <summary>Immutable snapshot of one subsystem's health.</summary>
/// <param name="Name">The subsystem name (for example <c>telemetry</c>).</param>
/// <param name="State">The current state.</param>
/// <param name="Since">When the current state was entered.</param>
/// <param name="Reason">Why the subsystem is degraded (or why it was restored), when known.</param>
public sealed record SubsystemStatus(string Name, SubsystemState State, DateTimeOffset Since, string? Reason);

/// <summary>
/// The RF-012/RNF-013 isolation contract: subsystems register here and report degraded or
/// restored state so failures are logged once per transition and the rest of the panel can
/// keep working. Deliberately a small concrete registry — no event bus, no observers.
/// Thread-safe; transitions are recorded with the injected <see cref="TimeProvider"/> (DD-14).
/// </summary>
public sealed class SubsystemHealth
{
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, SubsystemStatus> _subsystems = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SubsystemHealth"/> class.
    /// </summary>
    /// <param name="logger">The application logger; transitions are logged with a <c>SubsystemHealth</c> category.</param>
    /// <param name="timeProvider">Clock used for transition timestamps; defaults to <see cref="TimeProvider.System"/>.</param>
    public SubsystemHealth(ILogger logger, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger.ForContext<SubsystemHealth>();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Registers a subsystem as healthy. Registering an already known subsystem is a no-op.
    /// </summary>
    /// <param name="subsystem">The subsystem name.</param>
    /// <returns><see langword="true"/> when the subsystem was newly registered.</returns>
    public bool Register(string subsystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subsystem);

        bool added;
        lock (_gate)
        {
            added = !_subsystems.ContainsKey(subsystem);
            if (added)
            {
                _subsystems[subsystem] = new SubsystemStatus(
                    subsystem, SubsystemState.Healthy, _timeProvider.GetUtcNow(), Reason: null);
            }
        }

        if (added)
        {
            _logger.Debug("Subsystem {Subsystem} registered", subsystem);
        }

        return added;
    }

    /// <summary>
    /// Marks a subsystem degraded and logs the transition once. Unknown subsystems are
    /// auto-registered so an isolated failure is never lost because registration was missed.
    /// </summary>
    /// <param name="subsystem">The subsystem name.</param>
    /// <param name="reason">Why the subsystem is degraded; required so logs stay actionable.</param>
    /// <param name="exception">The failure, when one is available.</param>
    /// <returns><see langword="true"/> when the state changed; <see langword="false"/> when it was already degraded.</returns>
    public bool MarkDegraded(string subsystem, string reason, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subsystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        bool changed;
        lock (_gate)
        {
            changed = !_subsystems.TryGetValue(subsystem, out var existing)
                || existing.State != SubsystemState.Degraded;

            if (changed)
            {
                _subsystems[subsystem] = new SubsystemStatus(
                    subsystem, SubsystemState.Degraded, _timeProvider.GetUtcNow(), reason);
            }
        }

        if (changed)
        {
            _logger.Warning(exception, "Subsystem {Subsystem} degraded: {Reason}", subsystem, reason);
        }

        return changed;
    }

    /// <summary>
    /// Restores a subsystem to healthy and logs the transition once. Unknown subsystems
    /// are auto-registered as healthy.
    /// </summary>
    /// <param name="subsystem">The subsystem name.</param>
    /// <param name="reason">Optional context for the recovery.</param>
    /// <returns><see langword="true"/> when the state changed; <see langword="false"/> when it was already healthy.</returns>
    public bool MarkRestored(string subsystem, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subsystem);

        bool changed;
        lock (_gate)
        {
            changed = !_subsystems.TryGetValue(subsystem, out var existing)
                || existing.State != SubsystemState.Healthy;

            if (changed)
            {
                _subsystems[subsystem] = new SubsystemStatus(
                    subsystem, SubsystemState.Healthy, _timeProvider.GetUtcNow(), reason);
            }
        }

        if (changed)
        {
            _logger.Information("Subsystem {Subsystem} restored: {Reason}", subsystem, reason ?? "recovered");
        }

        return changed;
    }

    /// <summary>Gets the current status of a subsystem, or <see langword="null"/> when it is unknown.</summary>
    /// <param name="subsystem">The subsystem name.</param>
    /// <returns>An immutable status snapshot.</returns>
    public SubsystemStatus? GetStatus(string subsystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subsystem);

        lock (_gate)
        {
            return _subsystems.TryGetValue(subsystem, out var status) ? status : null;
        }
    }

    /// <summary>Determines whether a subsystem is currently degraded.</summary>
    /// <param name="subsystem">The subsystem name.</param>
    /// <returns><see langword="true"/> when the subsystem is registered and degraded.</returns>
    public bool IsDegraded(string subsystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subsystem);

        lock (_gate)
        {
            return _subsystems.TryGetValue(subsystem, out var status)
                && status.State == SubsystemState.Degraded;
        }
    }

    /// <summary>Gets status snapshots for all known subsystems, ordered by name.</summary>
    /// <returns>An immutable, deterministic snapshot of the registry.</returns>
    public IReadOnlyList<SubsystemStatus> GetAllStatuses()
    {
        lock (_gate)
        {
            return _subsystems.Values.OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
