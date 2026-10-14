using System.Runtime.InteropServices;

namespace TouchPanel.Interop;

/// <summary>
/// Raw user32 entry points required to open a tray context menu reliably (RNF-010: all P/Invoke
/// stays under <c>Interop/</c>). The standard tray-menu pattern makes the icon's hidden window the
/// foreground window immediately before showing the popup, so the popup receives the dismissing
/// click and closes as expected. The window is a hidden <c>WS_EX_NOACTIVATE</c> tool window, so
/// this foreground transfer belongs to the user-initiated tray click only and never activates the
/// panel (RF-018/RF-024).
/// </summary>
internal static class TrayMenuNative
{
    /// <summary>
    /// Brings the tray message window to the foreground (<c>SetForegroundWindow</c>). Best effort:
    /// the foreground lock may reject the request, in which case the popup still opens but the
    /// shell may not close it on an outside click.
    /// </summary>
    /// <param name="window">The tray icon's hidden message-window handle.</param>
    /// <returns><see langword="true"/> when the window was brought to the foreground.</returns>
    internal static bool SetForegroundWindow(nint window) =>
        window != 0 && SetForegroundWindowNative(window);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindowNative(nint window);
}
