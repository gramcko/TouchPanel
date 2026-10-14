namespace TouchPanel.Services.Display;

/// <summary>
/// Seam over the OS display-change notification so <see cref="DisplayChangeWatcher"/> can be
/// tested (and exercised) without the real <see cref="Microsoft.Win32.SystemEvents"/> plumbing
/// (RNF-006). The production implementation is <see cref="SystemEventsDisplayChangeSource"/>;
/// tests provide a fake whose <see cref="Changed"/> event they raise directly.
/// </summary>
public interface IDisplayChangeSource : IDisposable
{
    /// <summary>
    /// Raised when the OS reports a display-configuration change (added/removed display or a
    /// resolution change). Live display-scaling changes are additionally covered by the panel
    /// window's <c>WM_DPICHANGED</c> handling (RF-021). The event may be raised on any thread;
    /// marshalling is the responsibility of <see cref="DisplayChangeWatcher"/>.
    /// </summary>
    event EventHandler? Changed;
}
