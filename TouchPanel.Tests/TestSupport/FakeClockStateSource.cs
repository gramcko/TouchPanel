using TouchPanel.Services.Time;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Deterministic in-memory <see cref="IClockStateSource"/> (RF-046/M3-RF-463): the test controls
/// exactly when a system clock/time-zone/power/session change is raised, so the Clock widget's
/// boundary recomputation is verified without any OS subscription or wall-clock wait.
/// </summary>
internal sealed class FakeClockStateSource : IClockStateSource
{
    /// <inheritdoc />
    public event Action? StateChanged;

    /// <summary>Gets the number of times a state change was raised.</summary>
    public int RaiseCount { get; private set; }

    /// <summary>Raises <see cref="StateChanged"/> synchronously on the calling thread.</summary>
    public void Raise()
    {
        RaiseCount++;
        StateChanged?.Invoke();
    }
}
