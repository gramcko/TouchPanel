namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// Boundary for choosing an executable file for the run-command ("open program") action
/// (M2-RF-434, MD-13). The edit-mode action picker depends only on this seam so the browse model
/// stays unit-testable without a dialog; the production implementation wraps the WPF
/// <c>OpenFileDialog</c> and lives in <c>Views/Edit</c>.
/// </summary>
public interface IExecutablePicker
{
    /// <summary>
    /// Shows the executable picker and returns the chosen path.
    /// </summary>
    /// <returns>The absolute path to the chosen executable, or <see langword="null"/> when the user cancels.</returns>
    string? PickExecutable();
}
