using System.Runtime.InteropServices;

namespace TouchPanel.Interop.Input;

/// <summary>
/// Raw user32 entry points for global hotkey registration (RF-058, M2-RF-582). All hotkey
/// P/Invoke lives here under <c>Interop/</c> (RNF-010/M2-RNF-125); wrapped by
/// <see cref="WindowsHotKeyRegistration"/>.
/// </summary>
internal static class HotKeyNative
{
    /// <summary>The <c>WM_HOTKEY</c> message posted when a registered hotkey is pressed.</summary>
    internal const int WmHotKey = 0x0312;

    /// <summary>
    /// Registers a system-wide hotkey on the given window's thread.
    /// </summary>
    /// <param name="window">The window whose message queue receives <c>WM_HOTKEY</c>.</param>
    /// <param name="id">The caller-defined registration id (returned in <c>wParam</c>).</param>
    /// <param name="modifiers">The <c>MOD_*</c> modifier flags.</param>
    /// <param name="virtualKey">The Win32 virtual-key code.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> when the combination is already taken or invalid.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    /// <summary>
    /// Releases a hotkey previously registered by <see cref="RegisterHotKey"/> for this window's thread.
    /// </summary>
    /// <param name="window">The window the hotkey was registered on.</param>
    /// <param name="id">The registration id.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);
}
