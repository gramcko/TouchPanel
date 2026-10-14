namespace TouchPanel.Services.Tray;

/// <summary>
/// The RF-013 bounded retry/backoff policy for a failed tray registration: a fixed number of
/// attempts whose delay grows exponentially from <see cref="InitialDelay"/> by
/// <see cref="BackoffFactor"/> and is capped at <see cref="MaxDelay"/>. Pure math, no timing
/// side effects, so the schedule is unit-testable on its own (DD-14); <see cref="TrayService"/>
/// drives the actual schedule through the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class TrayRegistrationRetryPolicy
{
    /// <summary>Default number of retries after the initial registration attempt (RF-013).</summary>
    public const int DefaultMaxAttempts = 5;

    /// <summary>Default delay before the first retry.</summary>
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>Default exponential growth factor between retries.</summary>
    public const double DefaultBackoffFactor = 2.0;

    /// <summary>Default upper bound for a single retry delay.</summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>The policy used when none is supplied: 5 retries at 1 s, 2 s, 4 s, 8 s, 16 s.</summary>
    public static TrayRegistrationRetryPolicy Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayRegistrationRetryPolicy"/> class.
    /// </summary>
    /// <param name="maxAttempts">Maximum number of retries after the initial attempt; 0 disables retries.</param>
    /// <param name="initialDelay">Delay before the first retry; defaults to <see cref="DefaultInitialDelay"/>.</param>
    /// <param name="backoffFactor">Exponential growth factor (1 keeps the delay constant); defaults to <see cref="DefaultBackoffFactor"/>.</param>
    /// <param name="maxDelay">Upper bound for a single delay; defaults to <see cref="DefaultMaxDelay"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is outside its valid range.</exception>
    public TrayRegistrationRetryPolicy(
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? initialDelay = null,
        double backoffFactor = DefaultBackoffFactor,
        TimeSpan? maxDelay = null)
    {
        if (maxAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "The retry count cannot be negative.");
        }

        var initial = initialDelay ?? DefaultInitialDelay;
        if (initial <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay), initial, "The initial delay must be positive.");
        }

        if (backoffFactor < 1.0 || double.IsNaN(backoffFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(backoffFactor), backoffFactor, "The backoff factor must be at least 1.");
        }

        var max = maxDelay ?? DefaultMaxDelay;
        if (max <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), max, "The maximum delay must be positive.");
        }

        MaxAttempts = maxAttempts;
        InitialDelay = initial;
        BackoffFactor = backoffFactor;
        MaxDelay = max;
    }

    /// <summary>Gets the maximum number of retries after the initial registration attempt.</summary>
    public int MaxAttempts { get; }

    /// <summary>Gets the delay before the first retry.</summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>Gets the exponential growth factor between retries.</summary>
    public double BackoffFactor { get; }

    /// <summary>Gets the upper bound for a single retry delay.</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>
    /// Gets the delay before the given retry. The result can exceed <see cref="MaxDelay"/> only
    /// by floating-point rounding, never by design.
    /// </summary>
    /// <param name="attempt">1-based retry number (<c>1</c> is the first retry after the initial attempt).</param>
    /// <returns>The delay to wait before the retry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="attempt"/> is less than 1.</exception>
    public TimeSpan GetDelay(int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Retry attempts are 1-based.");
        }

        var milliseconds = InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt - 1);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, MaxDelay.TotalMilliseconds));
    }
}
