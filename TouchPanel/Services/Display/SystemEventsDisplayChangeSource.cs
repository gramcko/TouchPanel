using Microsoft.Win32;

namespace TouchPanel.Services.Display;

/// <summary>
/// Production <see cref="IDisplayChangeSource"/> backed by
/// <see cref="SystemEvents.DisplaySettingsChanged"/> (RF-022). This is the only place that touches
/// the OS display-change notification; the event it forwards is raised on the SystemEvents
/// notification thread and is marshalled to the UI thread by <see cref="DisplayChangeWatcher"/>.
/// </summary>
/// <remarks>
/// No timer, no polling: the notification is driven exclusively by the OS broadcast, satisfying
/// RNF-008. Subscribing is safe from the composition root's UI thread; <see cref="Dispose"/>
/// unsubscribes deterministically so no handler survives shutdown.
/// </remarks>
public sealed class SystemEventsDisplayChangeSource : IDisplayChangeSource
{
    private readonly EventHandler _handler;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemEventsDisplayChangeSource"/> class and
    /// subscribes to <see cref="SystemEvents.DisplaySettingsChanged"/>.
    /// </summary>
    public SystemEventsDisplayChangeSource()
    {
        _handler = OnDisplaySettingsChanged;
        SystemEvents.DisplaySettingsChanged += _handler;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            SystemEvents.DisplaySettingsChanged -= _handler;
        }
        catch (Exception)
        {
            // SystemEvents unsubscribe failures must never reach the shutdown path; the static
            // event is process-wide and is released with the process.
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);
}
