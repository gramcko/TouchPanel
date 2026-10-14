namespace TouchPanel.Services.Time;

/// <summary>
/// A testable seam over the operating system's clock/state notifications (RF-046/M3-RF-463). The
/// Clock view model subscribes to <see cref="StateChanged"/> and recomputes its next refresh
/// boundary when the system clock, time zone, power state, or session changes — event-driven, with
/// no polling. The production implementation is <see cref="SystemEventsClockStateSource"/>; tests
/// inject a fake so no OS subscription or wall-clock wait is required (RNF-006/M3-RNF-133).
/// </summary>
public interface IClockStateSource
{
    /// <summary>
    /// Occurs when the system clock/time zone changed or the machine resumed/suspended or the
    /// session switched. Handlers run on the raising thread and must marshal any UI work.
    /// </summary>
    event Action? StateChanged;
}
