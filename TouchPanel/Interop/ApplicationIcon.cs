using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TouchPanel.Interop;

/// <summary>
/// Provides the application's own icon as a native <c>HICON</c> for the notification area. The icon
/// is extracted from the running executable (the PE icon compiled from
/// <c>Assets\touchpanel.ico</c>), which also works for a single-file publish because
/// <see cref="Environment.ProcessPath"/> resolves to the launcher executable. The returned handle
/// is owned by the caller for the process lifetime and must not be destroyed while the tray uses
/// it. Any failure degrades to the shared system application icon, exactly as before.
/// </summary>
internal static class ApplicationIcon
{
    /// <summary>
    /// Loads the tray icon from the current process executable's icon resources, falling back to
    /// the stock application icon when extraction is unavailable or returns no handle.
    /// </summary>
    /// <returns>The extracted (process-lifetime) icon handle, or a shared stock icon handle.</returns>
    internal static nint LoadTrayIcon() => LoadTrayIcon(Environment.ProcessPath, ExtractSmallIcon);

    /// <summary>
    /// Test seam: loads the tray icon from <paramref name="executablePath"/> using
    /// <paramref name="extractSmallIcon"/>, falling back to <see cref="StockIcon.LoadApplicationIcon"/>
    /// on any failure, empty path, or zero handle.
    /// </summary>
    /// <param name="executablePath">Path of the executable whose icon should be extracted.</param>
    /// <param name="extractSmallIcon">Extractor used to obtain the small icon handle.</param>
    /// <returns>The extracted handle, or a shared stock icon handle.</returns>
    internal static nint LoadTrayIcon(string? executablePath, Func<string, nint> extractSmallIcon)
    {
        try
        {
            if (!string.IsNullOrEmpty(executablePath))
            {
                nint handle = extractSmallIcon(executablePath);
                if (handle != 0)
                {
                    return handle;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"TouchPanel: extracting the application icon failed ({ex.GetType().Name}: {ex.Message}); using the stock icon");
        }

        return StockIcon.LoadApplicationIcon();
    }

    /// <summary>
    /// Extracts the executable's first small icon. The unused large handle is released; the small
    /// handle is returned to the caller and intentionally outlives this call.
    /// </summary>
    private static nint ExtractSmallIcon(string executablePath)
    {
        nint large = 0;
        nint small = 0;
        uint extracted = ExtractIconEx(executablePath, 0, out large, out small, 1);

        // Only the small icon is displayed. Releasing the unused large handle avoids a second
        // process-lifetime allocation; the small handle is owned by the tray until process exit.
        if (large != 0 && large != small)
        {
            DestroyIcon(large);
        }

        return extracted != 0 && small != 0 ? small : 0;
    }

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string fileName,
        int iconIndex,
        out nint largeIcon,
        out nint smallIcon,
        uint iconCount);

    [DllImport("user32.dll", EntryPoint = "DestroyIcon", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
