using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IExecutablePicker"/> double for the T-14 action-picker tests: it returns a
/// programmable path and records how many times the browse was invoked, so the run-command browse
/// model is verified without opening a desktop dialog.
/// </summary>
internal sealed class FakeExecutablePicker : IExecutablePicker
{
    /// <summary>Gets or sets the path returned by the next browse; <see langword="null"/> simulates a cancel.</summary>
    public string? Result { get; set; }

    /// <summary>Gets the number of browse invocations.</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public string? PickExecutable()
    {
        CallCount++;
        return Result;
    }
}
