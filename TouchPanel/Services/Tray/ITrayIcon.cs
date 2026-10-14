using TouchPanel.Interop;
using TouchPanel.Services.Notifications;

namespace TouchPanel.Services.Tray;

/// <summary>
/// The seam over the native tray icon (T-05, DD-2). It exposes exactly the operations
/// <see cref="TrayService"/> needs, so the service can be tested without a shell or a hidden
/// window; production uses <see cref="TrayIconAdapter"/> over
/// <see cref="TouchPanel.Interop.TrayIcon"/>. Implementations must never activate a window
/// (RF-018/RF-024) and must be safe to call from the thread that owns the icon's window.
/// </summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>Gets a value indicating whether the shell currently shows the icon (RF-013).</summary>
    bool IsRegistered { get; }

    /// <summary>Gets the Win32 error code of the last unsuccessful shell call (0 after success).</summary>
    int LastError { get; }

    /// <summary>
    /// Gets the handle of the hidden window that owns the icon and receives its callbacks. Used by
    /// the menu host for the standard tray foreground pattern; 0 when no window exists.
    /// </summary>
    nint WindowHandle { get; }

    /// <summary>
    /// Registers the icon with the shell (<c>NIM_ADD</c>) with the callback message, icon, and
    /// tooltip set.
    /// </summary>
    /// <param name="iconHandle">Handle of the icon to display; 0 uses the shell default.</param>
    /// <param name="tooltip">Tray tooltip; values beyond the Win32 limit are truncated.</param>
    /// <returns><see langword="true"/> when the shell accepted the registration.</returns>
    bool Add(nint iconHandle, string? tooltip = null);

    /// <summary>
    /// Re-registers the icon with its last settings (<c>NIM_ADD</c>); the recovery path after the
    /// shell broadcasts a taskbar recreation (RF-013). Fails without a shell call when no icon
    /// has ever been registered.
    /// </summary>
    /// <returns><see langword="true"/> when the shell accepted the re-registration.</returns>
    bool ReAdd();

    /// <summary>Removes the icon from the shell (<c>NIM_DELETE</c>); safe to call repeatedly.</summary>
    /// <returns><see langword="true"/> when the icon is no longer registered.</returns>
    bool Delete();

    /// <summary>
    /// Shows a balloon notification on the icon (<c>NIF_INFO</c>) without taking foreground
    /// focus. Returns <see langword="false"/> while the icon is not registered so callers can
    /// fall back to logging (RF-010).
    /// </summary>
    /// <param name="title">Balloon title; values beyond the Win32 limit are truncated.</param>
    /// <param name="message">Balloon body; values beyond the Win32 limit are truncated.</param>
    /// <param name="severity">Relative urgency, mapped to the balloon icon.</param>
    /// <returns><see langword="true"/> when the shell accepted the balloon.</returns>
    bool ShowBalloon(string title, string message, NotificationSeverity severity);

    /// <summary>Raised for menu-relevant mouse messages on the icon (RF-005/RF-024).</summary>
    event EventHandler<TrayIconMouseEventArgs>? MouseAction;

    /// <summary>Raised when the shell broadcasts <c>TaskbarCreated</c> (Explorer restart, RF-013).</summary>
    event EventHandler? TaskbarCreated;
}
