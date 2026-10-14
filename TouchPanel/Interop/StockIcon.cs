using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TouchPanel.Interop;

/// <summary>
/// Loads shared system stock icons used as native resource handles (RF-005). The tray icon needs
/// a real <c>HICON</c> to display; M1 uses the shared default application icon until a dedicated
/// icon asset exists. Stock icons are owned by the system and must not be destroyed by callers.
/// All P/Invoke stays under <c>Interop/</c> (RNF-010).
/// </summary>
internal static class StockIcon
{
    private const int IdiApplication = 32512;

    /// <summary>
    /// Loads the shared default application icon (<c>IDI_APPLICATION</c>). Returns 0 when the
    /// system refuses the request; <c>Shell_NotifyIcon</c> then falls back to its own default.
    /// </summary>
    /// <returns>The shared icon handle, or 0 on failure.</returns>
    internal static nint LoadApplicationIcon()
    {
        try
        {
            return LoadIcon(0, new nint(IdiApplication));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TouchPanel: loading the stock application icon failed ({ex.GetType().Name}: {ex.Message})");
            return 0;
        }
    }

    [DllImport("user32.dll", EntryPoint = "LoadIconW", ExactSpelling = true)]
    private static extern nint LoadIcon(nint instance, nint iconName);
}
