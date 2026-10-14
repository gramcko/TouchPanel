using TouchPanel.Config;
using TouchPanel.Interop.Input;

namespace TouchPanel.Actions.Handlers;

/// <summary>
/// Mute toggle handler (RF-048): injects <c>VK_VOLUME_MUTE</c> through the <see cref="IKeyInput"/>
/// seam, matching DD-8's single input mechanism for volume keys.
/// </summary>
public sealed class MuteHandler : IActionHandler
{
    private readonly IKeyInput _input;

    /// <summary>
    /// Initializes a new instance of the <see cref="MuteHandler"/> class.
    /// </summary>
    /// <param name="input">The key-synthesis boundary.</param>
    public MuteHandler(IKeyInput input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <inheritdoc />
    public string ActionType => ActionTypes.Mute;

    /// <inheritdoc />
    public Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not MuteActionConfig)
        {
            throw new ArgumentException(
                $"The {nameof(MuteHandler)} requires a {nameof(MuteActionConfig)}; got {action.GetType().Name}.",
                nameof(action));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_input.TrySendKey(VirtualKeys.VolumeMute, out var error)
            ? ActionExecutionResult.Succeeded()
            : ActionExecutionResult.Failed(error ?? "Mute input synthesis failed."));
    }
}
