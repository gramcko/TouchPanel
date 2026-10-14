using System.Runtime.InteropServices;

namespace TouchPanel.Interop;

/// <summary>
/// Raw user32 entry points for the message-window mechanism (RNF-010, plan section 1.2:
/// all P/Invoke stays under <c>Interop/</c>). Wrapped by <see cref="MessageWindow"/>.
/// </summary>
internal static class MessageWindowNative
{
    /// <summary>The <c>HWND_MESSAGE</c> pseudo-handle used as the parent of message-only windows.</summary>
    internal static readonly nint HwndMessage = new(-3);

    /// <summary>Window style for a hidden top-level window (<c>WS_POPUP</c>, no caption/border).</summary>
    internal const int WsPopup = unchecked((int)0x80000000);

    /// <summary>Keeps a hidden top-level window out of the taskbar and Alt+Tab.</summary>
    internal const int WsExToolWindow = 0x00000080;

    /// <summary>Prevents a hidden top-level window from ever being activated (RF-018).</summary>
    internal const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint RegisterWindowMessage(string messageName);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
