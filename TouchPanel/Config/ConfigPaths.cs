using System.Globalization;
using System.IO;
using TouchPanel.Services;

namespace TouchPanel.Config;

/// <summary>
/// Resolves every file path the configuration subsystem owns (RF-073, D-12): the user-editable
/// <c>panel.json</c>, the last-known-valid <c>panel.backup.json</c>, the runtime
/// <c>state.json</c>, and the preserved corrupt-source pattern. The data directory is injectable
/// so tests never touch the real user profile (RNF-006).
/// </summary>
public sealed class ConfigPaths
{
    /// <summary>File name of the user-editable configuration (RF-073).</summary>
    public const string PanelFileName = "panel.json";

    /// <summary>File name of the last known valid backup (RF-078).</summary>
    public const string BackupFileName = "panel.backup.json";

    /// <summary>File name of the non-user-facing runtime flag store (RF-073).</summary>
    public const string StateFileName = "state.json";

    /// <summary>File name of the best-effort emergency copy written when a shutdown save fails (F-08/RF-077).</summary>
    public const string RecoveryFileName = "panel.recovery.json";

    /// <summary>Prefix used for preserved corrupt or unsupported sources (RF-075, F-04).</summary>
    public const string CorruptFileNamePrefix = "panel.corrupt-";

    /// <summary>
    /// Prefix used for timestamped pre-import backups written before an imported configuration is
    /// applied (M2-RF-832, MD-11). The name is deliberately distinct from <c>panel.json</c> so the
    /// hot-reload watcher (<c>Filter = panel.json</c>) never treats a backup as a reload (M2-RF-821).
    /// </summary>
    public const string ImportBackupFileNamePrefix = "panel.import-backup-";

    /// <summary>Search pattern matching preserved corrupt sources.</summary>
    public const string CorruptFileSearchPattern = CorruptFileNamePrefix + "*.json";

    /// <summary>Paths resolved against the current user's roaming application data (D-5, D-12).</summary>
    public static ConfigPaths Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigPaths"/> class.
    /// </summary>
    /// <param name="dataDirectory">
    /// Directory that holds the configuration files; when <see langword="null"/> or whitespace,
    /// <c>%APPDATA%\TouchPanel</c> (via <see cref="AppPaths"/>) is used.
    /// </param>
    public ConfigPaths(string? dataDirectory = null)
    {
        DataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
            ? AppPaths.Default.DataDirectory
            : Path.GetFullPath(dataDirectory);
    }

    /// <summary>Gets the configuration directory (<c>%APPDATA%\TouchPanel</c> by default).</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the path of the user-editable configuration file (RF-073).</summary>
    public string PanelFile => Path.Combine(DataDirectory, PanelFileName);

    /// <summary>Gets the path of the last known valid backup (RF-078).</summary>
    public string BackupFile => Path.Combine(DataDirectory, BackupFileName);

    /// <summary>Gets the path of the runtime flag store (RF-073).</summary>
    public string StateFile => Path.Combine(DataDirectory, StateFileName);

    /// <summary>
    /// Gets the path of the best-effort emergency recovery copy (F-08/RF-077). It is only written
    /// when a save during shutdown fails, never as part of a normal save.
    /// </summary>
    public string RecoveryFile => Path.Combine(DataDirectory, RecoveryFileName);

    /// <summary>
    /// Builds the path that preserves a corrupt or unsupported source for diagnosis (RF-075,
    /// RF-078). The caller is responsible for choosing a free name when the timestamp collides.
    /// </summary>
    /// <param name="timestamp">Timestamp embedded in the file name (for example <c>2026-10-08T14:30:12.345+02:00</c>).</param>
    /// <returns>The full path of the preservation file.</returns>
    public string GetCorruptFilePath(DateTimeOffset timestamp)
    {
        var name = CorruptFileNamePrefix
            + timestamp.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)
            + ".json";
        return Path.Combine(DataDirectory, name);
    }

    /// <summary>
    /// Builds the path of a timestamped pre-import backup of the current effective configuration
    /// (M2-RF-832, MD-11). The caller is responsible for choosing a free name when the timestamp
    /// collides.
    /// </summary>
    /// <param name="timestamp">Timestamp embedded in the file name (for example <c>2026-10-09T14:30:12.345+02:00</c>).</param>
    /// <returns>The full path of the pre-import backup file.</returns>
    public string GetImportBackupFilePath(DateTimeOffset timestamp)
    {
        var name = ImportBackupFileNamePrefix
            + timestamp.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)
            + ".json";
        return Path.Combine(DataDirectory, name);
    }
}
