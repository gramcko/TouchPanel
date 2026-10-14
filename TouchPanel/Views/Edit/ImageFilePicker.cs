using Microsoft.Win32;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Views.Edit;

/// <summary>
/// WPF implementation of the Image widget's file browse seam (user-requested addition). It shows a
/// standard open-file dialog filtered to raster image formats (PNG/JPEG/BMP/GIF/WebP and the other
/// formats <c>BitmapImage</c> can decode) and returns the chosen absolute path. This is view-layer
/// code (a dialog); the property editor depends only on <see cref="IImageFilePicker"/>, so the
/// browse model stays unit-testable without a desktop.
/// </summary>
public sealed class ImageFilePicker : IImageFilePicker
{
    /// <inheritdoc />
    public string? PickImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp"
                + "|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
