using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IImageFilePicker"/> double for the Image-widget browse tests: it returns a
/// programmable path and records how many times the browse was invoked, so the image browse model is
/// verified without opening a desktop dialog.
/// </summary>
internal sealed class FakeImageFilePicker : IImageFilePicker
{
    /// <summary>Gets or sets the path returned by the next browse; <see langword="null"/> simulates a cancel.</summary>
    public string? Result { get; set; }

    /// <summary>Gets the number of browse invocations.</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public string? PickImage()
    {
        CallCount++;
        return Result;
    }
}
