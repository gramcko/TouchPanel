namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// Boundary for choosing an image file for the Image widget's path field (user-requested addition).
/// The property editor depends only on this seam so the browse model stays unit-testable without a
/// dialog; the production implementation wraps the WPF <c>OpenFileDialog</c> with an image filter
/// and lives in <c>Views/Edit</c>, mirroring <see cref="IExecutablePicker"/>.
/// </summary>
public interface IImageFilePicker
{
    /// <summary>
    /// Shows the image picker and returns the chosen path.
    /// </summary>
    /// <returns>The absolute path to the chosen image file, or <see langword="null"/> when the user cancels.</returns>
    string? PickImage();
}
