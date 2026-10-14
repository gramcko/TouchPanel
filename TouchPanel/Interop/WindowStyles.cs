using System.Runtime.InteropServices;

namespace TouchPanel.Interop;

/// <summary>
/// Raw user32 window-style and window-positioning entry points used by the panel window
/// (DD-5; RNF-010: all P/Invoke stays under <c>Interop/</c>). Contains no WPF types and no
/// business logic: the caller (<c>MainWindow</c>) owns the policy, this class only marshals.
/// </summary>
/// <remarks>
/// <para><b>No activation.</b> Every operation here is paired with the no-activate flag or
/// command (<see cref="SwpNoActivate"/>, <see cref="SwShowNoActivate"/>), and the application
/// never calls <c>SetForegroundWindow</c> in normal mode (RF-018).</para>
/// <para><b>Physical pixels.</b> Coordinates passed to <see cref="SetWindowPos"/> are physical
/// screen pixels in virtual-desktop coordinates, matching <c>GetMonitorInfo</c> bounds; no DIP
/// conversion happens here (DD-5, R-4).</para>
/// <para><b>Architecture.</b> The process is x64 (RF-090); the 32-bit entry points are used only
/// when the helpers run inside a 32-bit process, where the <c>*Ptr</c> exports do not exist.</para>
/// </remarks>
internal static class WindowStyles
{
    /// <summary><c>GWL_EXSTYLE</c>: index of the extended window style (<c>GetWindowLongPtr</c>/<c>SetWindowLongPtr</c>).</summary>
    internal const int GwlExStyle = -20;

    /// <summary><c>WS_EX_NOACTIVATE</c>: the window never becomes the foreground window (RF-018).</summary>
    internal const int WsExNoActivate = 0x08000000;

    /// <summary><c>WS_EX_TOOLWINDOW</c>: the window is absent from the taskbar and Alt+Tab (RF-019).</summary>
    internal const int WsExToolWindow = 0x00000080;

    /// <summary><c>SWP_NOSIZE</c>: keep the current size.</summary>
    internal const uint SwpNoSize = 0x0001;

    /// <summary><c>SWP_NOMOVE</c>: keep the current position.</summary>
    internal const uint SwpNoMove = 0x0002;

    /// <summary><c>SWP_NOZORDER</c>: keep the current Z-order.</summary>
    internal const uint SwpNoZOrder = 0x0004;

    /// <summary><c>SWP_NOACTIVATE</c>: never activate the window (RF-018).</summary>
    internal const uint SwpNoActivate = 0x0010;

    /// <summary><c>HWND_TOPMOST</c>: place the window above all non-topmost windows (RF-020).</summary>
    internal static readonly nint HwndTopmost = new(-1);

    /// <summary><c>HWND_NOTOPMOST</c>: return the window to the non-topmost band (RF-020).</summary>
    internal static readonly nint HwndNotTopmost = new(-2);

    /// <summary><c>SW_SHOWNOACTIVATE</c>: show the window without activating it (DD-5).</summary>
    internal const int SwShowNoActivate = 4;

    /// <summary><c>SW_HIDE</c>: hide the window (DD-5).</summary>
    internal const int SwHide = 0;

    /// <summary><c>WM_DPICHANGED</c> (0x02E0): the DPI of the monitor hosting the window changed (RF-021/RF-022).</summary>
    internal const int WmDpiChanged = 0x02E0;

    /// <summary>
    /// Reads a window long value. With <paramref name="index"/> = <see cref="GwlExStyle"/> this
    /// returns the extended window style bits.
    /// </summary>
    /// <param name="window">The target window handle.</param>
    /// <param name="index">The value to read (<c>GWL_*</c>).</param>
    /// <returns>The current value; 0 also on failure (check <c>Marshal.GetLastWin32Error</c> when it matters).</returns>
    internal static nint GetWindowLongPtr(nint window, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtrW(window, index)
            : (nint)GetWindowLongW(window, index);

    /// <summary>
    /// Writes a window long value and returns the previous one.
    /// </summary>
    /// <param name="window">The target window handle.</param>
    /// <param name="index">The value to write (<c>GWL_*</c>).</param>
    /// <param name="value">The new value.</param>
    /// <returns>The previous value; 0 also on failure.</returns>
    internal static nint SetWindowLongPtr(nint window, int index, nint value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtrW(window, index, value)
            : (nint)SetWindowLongW(window, index, (int)value);

    /// <summary>
    /// Adds extended style bits to a window (<c>OR</c> semantics) and returns the resulting
    /// extended style value. Used for <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> (RF-018/RF-019).
    /// </summary>
    /// <param name="window">The target window handle.</param>
    /// <param name="styles">The extended style bits to add.</param>
    /// <returns>The new extended style value after the update.</returns>
    internal static nint AddExtendedStyles(nint window, int styles)
    {
        var current = GetWindowLongPtr(window, GwlExStyle);
        return SetWindowLongPtr(window, GwlExStyle, current | styles);
    }

    /// <summary>
    /// Moves, resizes, or reorders a window. Callers pass <see cref="SwpNoActivate"/> on every
    /// panel operation so the call never activates the window (DD-5/RF-018).
    /// </summary>
    /// <param name="window">The target window handle.</param>
    /// <param name="insertAfter">The Z-order reference (<c>HWND_*</c> pseudo-handle) or 0 when <see cref="SwpNoZOrder"/> is set.</param>
    /// <param name="x">Left edge in physical pixels.</param>
    /// <param name="y">Top edge in physical pixels.</param>
    /// <param name="width">Width in physical pixels.</param>
    /// <param name="height">Height in physical pixels.</param>
    /// <param name="flags"><c>SWP_*</c> flags.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    internal static bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags) =>
        SetWindowPosNative(window, insertAfter, x, y, width, height, flags);

    /// <summary>
    /// Shows or hides a window. Panel callers pass <see cref="SwShowNoActivate"/> to show without
    /// activation (DD-5) and <see cref="SwHide"/> to hide.
    /// </summary>
    /// <param name="window">The target window handle.</param>
    /// <param name="command">The <c>SW_*</c> show command.</param>
    /// <returns><see langword="true"/> when the window was previously visible.</returns>
    internal static bool ShowWindow(nint window, int command) => ShowWindowNative(window, command);

    /// <summary>
    /// Reads the DPI of the monitor hosting the window (<c>GetDpiForWindow</c>, Windows 10 1607+).
    /// The display-change handler uses it to convert the target monitor's physical bounds into the
    /// DIP page bounds widgets are clamped against (RF-022), including while the window is hidden.
    /// </summary>
    /// <param name="window">The target window handle.</param>
    /// <returns>The monitor DPI (for example 96, 120, or 144), or 0 when it cannot be read.</returns>
    internal static uint GetDpiForWindow(nint window) => GetDpiForWindowNative(window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(nint window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPosNative(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowNative(nint window, int command);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow", SetLastError = true)]
    private static extern uint GetDpiForWindowNative(nint window);
}
