namespace TouchPanel.Actions;

/// <summary>
/// Outcome of one action execution, returned across the <see cref="IActionExecutor"/> boundary to
/// the widget that requested it (RF-050, RF-036). Immutable and WPF-free (RF-053); the message is
/// always safe to log and display because <see cref="ActionExecutor"/> redacts it (RF-051/RF-085).
/// </summary>
public sealed record ActionExecutionResult
{
    private ActionExecutionResult(bool success, bool canceled, string message)
    {
        Success = success;
        Canceled = canceled;
        Message = message;
    }

    /// <summary>Gets a value indicating whether the action completed successfully.</summary>
    public bool Success { get; }

    /// <summary>
    /// Gets a value indicating whether execution was canceled before completing (for example during
    /// shutdown). A canceled result is not a failure and should not raise a widget error indicator.
    /// </summary>
    public bool Canceled { get; }

    /// <summary>Gets the human-readable, redaction-safe outcome message (empty on success).</summary>
    public string Message { get; }

    /// <summary>Creates a successful result.</summary>
    /// <returns>The success result.</returns>
    public static ActionExecutionResult Succeeded() => new(success: true, canceled: false, message: string.Empty);

    /// <summary>Creates a failed result with a redaction-safe reason.</summary>
    /// <param name="message">Why the action failed; a generic message is substituted when empty.</param>
    /// <returns>The failure result.</returns>
    public static ActionExecutionResult Failed(string? message) =>
        new(success: false, canceled: false, message: string.IsNullOrWhiteSpace(message) ? "The action failed." : message);

    /// <summary>Creates a canceled result.</summary>
    /// <returns>The canceled result.</returns>
    public static ActionExecutionResult CanceledResult() =>
        new(success: false, canceled: true, message: "The action was canceled.");
}
