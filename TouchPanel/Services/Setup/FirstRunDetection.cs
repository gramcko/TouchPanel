using System.IO;

namespace TouchPanel.Services.Setup;

/// <summary>
/// First-run detection for the setup wizard (WPF-free). The check must run <b>before</b>
/// <see cref="Config.JsonConfigStore"/> loads <c>panel.json</c>, because the store creates and
/// persists defaults when the file is missing; checking afterwards would always report "not first
/// run".
/// </summary>
public static class FirstRunDetection
{
    /// <summary>
    /// Determines whether this is the first run by testing whether the configuration file exists.
    /// </summary>
    /// <param name="panelFilePath">The full path of <c>panel.json</c>; must not be blank.</param>
    /// <returns><see langword="true"/> when the file does not exist (first run); otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="panelFilePath"/> is blank.</exception>
    public static bool IsFirstRun(string panelFilePath)
    {
        if (string.IsNullOrWhiteSpace(panelFilePath))
        {
            throw new ArgumentException("A configuration file path is required.", nameof(panelFilePath));
        }

        return !File.Exists(panelFilePath);
    }
}
