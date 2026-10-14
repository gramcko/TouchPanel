using TouchPanel.Services.Tray;

namespace TouchPanel.Tests.Services;

/// <summary>
/// RF-013 retry/backoff math: the bounded registration schedule is pure and deterministic, so
/// the exponential growth, the cap, and the input validation are tested without any timing.
/// </summary>
public sealed class TrayRegistrationRetryPolicyTests
{
    [Fact]
    public void Default_ProducesTheDocumentedBoundedSchedule()
    {
        var policy = TrayRegistrationRetryPolicy.Default;

        Assert.Equal(5, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.InitialDelay);
        Assert.Equal(2.0, policy.BackoffFactor);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxDelay);
    }

    [Fact]
    public void GetDelay_GrowsExponentiallyFromTheInitialDelay()
    {
        var policy = new TrayRegistrationRetryPolicy(
            maxAttempts: 5,
            initialDelay: TimeSpan.FromMilliseconds(250));

        Assert.Equal(TimeSpan.FromMilliseconds(250), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), policy.GetDelay(4));
        Assert.Equal(TimeSpan.FromMilliseconds(4000), policy.GetDelay(5));
    }

    [Fact]
    public void GetDelay_CapsAtTheMaximumDelay()
    {
        var policy = new TrayRegistrationRetryPolicy(
            maxAttempts: 10,
            initialDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetDelay(5));
    }

    [Fact]
    public void GetDelay_WithFactorOne_IsConstant()
    {
        var policy = new TrayRegistrationRetryPolicy(
            maxAttempts: 3,
            initialDelay: TimeSpan.FromSeconds(2),
            backoffFactor: 1.0);

        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(3));
    }

    [Fact]
    public void GetDelay_ZeroOrNegativeAttempt_Throws()
    {
        var policy = TrayRegistrationRetryPolicy.Default;

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelay(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelay(-1));
    }

    [Fact]
    public void Constructor_InvalidValues_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrayRegistrationRetryPolicy(maxAttempts: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrayRegistrationRetryPolicy(initialDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrayRegistrationRetryPolicy(backoffFactor: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrayRegistrationRetryPolicy(backoffFactor: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrayRegistrationRetryPolicy(maxDelay: TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_ZeroAttempts_DisablesRetries()
    {
        var policy = new TrayRegistrationRetryPolicy(maxAttempts: 0);

        Assert.Equal(0, policy.MaxAttempts);
    }
}
