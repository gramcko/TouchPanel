using TouchPanel.Config;

namespace TouchPanel.Actions;

/// <summary>
/// Executes one action config type (DD-8: the executor resolves a handler by the config
/// <c>type</c> discriminator). Handlers are registered by <see cref="ActionType"/> and may throw:
/// <see cref="ActionExecutor"/> contains failures and converts them to
/// <see cref="ActionExecutionResult"/> (RF-050).
/// </summary>
public interface IActionHandler
{
    /// <summary>Gets the action config <c>type</c> this handler executes (see <see cref="ActionTypes"/>).</summary>
    string ActionType { get; }

    /// <summary>
    /// Executes the action. Implementations may throw on invalid parameters or infrastructure
    /// failures; the calling <see cref="ActionExecutor"/> catches everything and maps it to a
    /// failed result, so no exception escapes the executor boundary (RF-050).
    /// </summary>
    /// <param name="action">The typed action configuration; the concrete type matches <see cref="ActionType"/>.</param>
    /// <param name="cancellationToken">Cancels in-flight execution.</param>
    /// <returns>The handler result.</returns>
    Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, CancellationToken cancellationToken);
}
