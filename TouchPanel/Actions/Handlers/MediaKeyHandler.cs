using TouchPanel.Config;
using TouchPanel.Interop.Input;

namespace TouchPanel.Actions.Handlers;

/// <summary>
/// Media key handler (RF-048): maps the configured <see cref="MediaKeyKind"/> to the matching
/// Win32 virtual key and injects it through the <see cref="IKeyInput"/> seam (the <c>SendInput</c>
/// implementation lives in <c>Interop/Input</c>). Media keys are system-wide and work while
/// another application owns the foreground (RF-057 context).
/// </summary>
public sealed class MediaKeyHandler : IActionHandler
{
    private readonly IKeyInput _input;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaKeyHandler"/> class.
    /// </summary>
    /// <param name="input">The key-synthesis boundary.</param>
    public MediaKeyHandler(IKeyInput input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <inheritdoc />
    public string ActionType => ActionTypes.MediaKey;

    /// <inheritdoc />
    public Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not MediaKeyActionConfig media)
        {
            throw new ArgumentException(
                $"The {nameof(MediaKeyHandler)} requires a {nameof(MediaKeyActionConfig)}; got {action.GetType().Name}.",
                nameof(action));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryMapKey(media.Key, out var virtualKey))
        {
            // RF-047 fail-fast: an unrecognized key was disabled at load and executes nothing.
            return Task.FromResult(ActionExecutionResult.Failed($"Unsupported media key '{media.Key}'."));
        }

        return Task.FromResult(_input.TrySendKey(virtualKey, out var error)
            ? ActionExecutionResult.Succeeded()
            : ActionExecutionResult.Failed(error ?? "Media key input synthesis failed."));
    }

    /// <summary>Maps a configured media key to its Win32 virtual key.</summary>
    /// <param name="kind">The configured media key.</param>
    /// <param name="virtualKey">The virtual key when mapping succeeds; 0 otherwise.</param>
    /// <returns><see langword="true"/> when the key is part of the M1 catalog.</returns>
    internal static bool TryMapKey(MediaKeyKind kind, out ushort virtualKey)
    {
        virtualKey = kind switch
        {
            MediaKeyKind.PlayPause => VirtualKeys.MediaPlayPause,
            MediaKeyKind.Next => VirtualKeys.MediaNextTrack,
            MediaKeyKind.Previous => VirtualKeys.MediaPreviousTrack,
            MediaKeyKind.Stop => VirtualKeys.MediaStop,
            _ => 0,
        };

        return virtualKey != 0;
    }
}
