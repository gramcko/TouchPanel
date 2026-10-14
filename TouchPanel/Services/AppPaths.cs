using System.IO;

namespace TouchPanel.Services;

/// <summary>
/// Resolves the per-user directories TouchPanel reads and writes (D-5, D-12).
/// The application-data roots are injectable so tests can work in temp directories
/// and never touch the real user profile (RNF-006).
/// </summary>
public sealed class AppPaths
{
    /// <summary>Directory name used under both the local and roaming application-data roots.</summary>
    public const string AppDirectoryName = "TouchPanel";

    /// <summary>Paths resolved against the current user's profile.</summary>
    public static AppPaths Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AppPaths"/> class.
    /// </summary>
    /// <param name="localAppDataRoot">
    /// Root that holds <c>%LOCALAPPDATA%\TouchPanel\logs</c>; when <see langword="null"/> or
    /// whitespace the current user's local application-data folder is used.
    /// </param>
    /// <param name="roamingAppDataRoot">
    /// Root that holds <c>%APPDATA%\TouchPanel</c>; when <see langword="null"/> or
    /// whitespace the current user's roaming application-data folder is used.
    /// </param>
    public AppPaths(string? localAppDataRoot = null, string? roamingAppDataRoot = null)
    {
        LocalAppDataRoot = Normalize(localAppDataRoot)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RoamingAppDataRoot = Normalize(roamingAppDataRoot)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        LogsDirectory = Path.Combine(LocalAppDataRoot, AppDirectoryName, "logs");
        DataDirectory = Path.Combine(RoamingAppDataRoot, AppDirectoryName);
    }

    /// <summary>Gets the root used for machine-local data (logs).</summary>
    public string LocalAppDataRoot { get; }

    /// <summary>Gets the root used for roaming data (configuration, runtime state).</summary>
    public string RoamingAppDataRoot { get; }

    /// <summary>Gets <c>%LOCALAPPDATA%\TouchPanel\logs</c>, the rolling log directory (D-5, DD-1).</summary>
    public string LogsDirectory { get; }

    /// <summary>Gets <c>%APPDATA%\TouchPanel</c>, the configuration and runtime-state directory (D-5, D-12).</summary>
    public string DataDirectory { get; }

    private static string? Normalize(string? root) =>
        string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
}
