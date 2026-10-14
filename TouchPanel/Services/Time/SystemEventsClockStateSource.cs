using Microsoft.Win32;
using Serilog;

namespace TouchPanel.Services.Time;

/// <summary>
/// Default <see cref="IClockStateSource"/> over the managed <see cref="SystemEvents"/> notifications
/// (RF-046/M3-RF-463): system time or time-zone change (<see cref="SystemEvents.TimeChanged"/>),
/// power suspend/resume (<see cref="SystemEvents.PowerModeChanged"/>), and session switch
/// (<see cref="SystemEvents.SessionSwitch"/>). It performs no polling and contains every failure.
/// </summary>
/// <remarks>
/// One instance is created by the composition root and shared by all Clock widgets for the lifetime
/// of the process; <see cref="Dispose"/> releases the OS subscriptions. This uses the managed
/// <see cref="SystemEvents"/> API only — there is no P/Invoke here (M3-RNF-134).
/// </remarks>
public sealed class SystemEventsClockStateSource : IClockStateSource, IDisposable
{
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemEventsClockStateSource"/> class and
    /// subscribes to the OS notifications.
    /// </summary>
    /// <param name="logger">Logger for isolated subscriber failures; defaults to the global Serilog logger.</param>
    public SystemEventsClockStateSource(ILogger? logger = null)
    {
        _logger = logger ?? Log.Logger;

        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <inheritdoc />
    public event Action? StateChanged;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    private void OnTimeChanged(object? sender, EventArgs e) => Raise();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Only the transitions that can change the displayed instant matter; status changes are
        // ignored so the Clock never wakes for an unrelated power report.
        if (e.Mode is PowerModes.Resume or PowerModes.Suspend)
        {
            Raise();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => Raise();

    private void Raise()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception exception)
        {
            // RF-012/M3-RNF-138: a bad subscriber is isolated; the Clock degrades to its last text.
            _logger.Warning(exception, "Clock: a clock-state subscriber failed");
        }
    }
}
