using Microsoft.Win32;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Views.Settings;

/// <summary>
/// WPF implementation of the configuration import/export file-picker seam (T-17, M2-RF-835). It
/// shows the standard open/save dialogs restricted to JSON by default and returns the chosen path.
/// This is deliberately view-layer code (a dialog); the settings view model depends only on
/// <see cref="IConfigFilePicker"/>, so the import/export flow stays unit-testable without a desktop.
/// </summary>
public sealed class ConfigFilePicker : IConfigFilePicker
{
    /// <inheritdoc />
    public string? PickImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import TouchPanel configuration",
            Filter = "TouchPanel configuration (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickExportPath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export TouchPanel configuration",
            Filter = "TouchPanel configuration (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "panel.json",
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
