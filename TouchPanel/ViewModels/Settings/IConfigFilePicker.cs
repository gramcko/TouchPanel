namespace TouchPanel.ViewModels.Settings;

/// <summary>
/// Boundary for choosing configuration file paths for import/export (T-17, M2-RF-835/M2-RF-921).
/// The settings view model depends only on this seam so the import/export flow stays unit-testable
/// without a dialog; the production implementation wraps the WPF <c>OpenFileDialog</c> /
/// <c>SaveFileDialog</c> and lives in <c>Views/Settings</c>.
/// </summary>
public interface IConfigFilePicker
{
    /// <summary>
    /// Shows the import picker and returns the chosen configuration file.
    /// </summary>
    /// <returns>The absolute path of the file to import, or <see langword="null"/> when the user cancels.</returns>
    string? PickImportPath();

    /// <summary>
    /// Shows the export picker and returns the chosen target path.
    /// </summary>
    /// <returns>The absolute path to export to, or <see langword="null"/> when the user cancels.</returns>
    string? PickExportPath();
}
