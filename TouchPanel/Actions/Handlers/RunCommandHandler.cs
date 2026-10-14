using TouchPanel.Config;
using TouchPanel.Services.Processes;

namespace TouchPanel.Actions.Handlers;

/// <summary>
/// Run command / launch application (RF-048, RF-049): builds a validated launch request through
/// <see cref="ProcessLaunchRequestFactory"/> and delegates to <see cref="IProcessLauncher"/>. The
/// process is started and left untracked (D-20/RF-052).
/// </summary>
public sealed class RunCommandHandler : IActionHandler
{
    private readonly IProcessLauncher _launcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunCommandHandler"/> class.
    /// </summary>
    /// <param name="launcher">The process launch boundary.</param>
    public RunCommandHandler(IProcessLauncher launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <inheritdoc />
    public string ActionType => ActionTypes.RunCommand;

    /// <inheritdoc />
    public async Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not RunCommandActionConfig run)
        {
            throw new ArgumentException(
                $"The {nameof(RunCommandHandler)} requires a {nameof(RunCommandActionConfig)}; got {action.GetType().Name}.",
                nameof(action));
        }

        var request = ProcessLaunchRequestFactory.Create(run);
        var result = await _launcher.LaunchAsync(request, cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ActionExecutionResult.Succeeded()
            : ActionExecutionResult.Failed(result.ErrorMessage ?? "The process could not be started.");
    }
}
