using Microsoft.Win32;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Views.Edit;

/// <summary>
/// WPF implementation of the run-command executable browse seam (M2-RF-434, MD-13). It shows a
/// standard open-file dialog restricted to <c>.exe</c> by default and returns the chosen path. This
/// is deliberately view-layer code (a dialog); the edit-mode view models depend only on
/// <see cref="IExecutablePicker"/>, so the browse model stays unit-testable without a desktop.
/// </summary>
public sealed class ExecutableFilePicker : IExecutablePicker
{
    /// <inheritdoc />
    public string? PickExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
