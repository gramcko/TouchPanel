using TouchPanel.Config;

namespace TouchPanel.Actions;

/// <summary>
/// Executes one configured action and returns a result instead of throwing (RF-050). The
/// application-level boundary widgets depend on (plan section 1.3); it has no WPF dependency so it
/// is testable without a UI (RF-053).
/// </summary>
public interface IActionExecutor
{
    /// <summary>
    /// Executes <paramref name="action"/> asynchronously and never lets an exception escape
    /// (RF-050). Failures are logged with the action type and widget id, then returned as a failed
    /// <see cref="ActionExecutionResult"/> so the widget can surface them (RF-036).
    /// </summary>
    /// <param name="action">The typed action configuration bound to the widget.</param>
    /// <param name="widgetId">The owning widget id, when known, for structured failure logs.</param>
    /// <param name="cancellationToken">Cancels in-flight execution (for example during shutdown).</param>
    /// <returns>The execution result; never <see langword="null"/>.</returns>
    Task<ActionExecutionResult> ExecuteAsync(
        ActionConfig action,
        string? widgetId = null,
        CancellationToken cancellationToken = default);
}
