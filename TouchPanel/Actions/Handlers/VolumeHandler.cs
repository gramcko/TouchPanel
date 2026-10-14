using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Interop.Input;

namespace TouchPanel.Actions.Handlers;

/// <summary>
/// Volume handler (RF-048): <see cref="VolumeMode.Step"/> injects <c>VK_VOLUME_UP</c>/<c>VK_VOLUME_DOWN</c>
/// through the <see cref="IKeyInput"/> seam, while <see cref="VolumeMode.Set"/> applies an absolute
/// level through the <see cref="IAudioController"/> seam.
/// </summary>
/// <remarks>
/// <b>Documented DD-8 deviation (T-09).</b> DD-8 specified "all media/volume via SendInput with
/// <c>VK_VOLUME_*</c>". Volume <i>steps</i> follow that exactly, but an absolute <c>set</c> cannot:
/// each volume key press advances by a device-defined increment and the current level is unknown,
/// so a configured level (for example 30) could not be reached accurately. Absolute set therefore
/// uses the Core Audio <c>IAudioEndpointVolume</c> API (no NuGet; COM interop under
/// <c>Interop/Audio</c>), keeping the handler logic unit-testable behind the
/// <see cref="IAudioController"/> seam. Mute stays on the key path (<c>VK_VOLUME_MUTE</c>).
/// </remarks>
public sealed class VolumeHandler : IActionHandler
{
    private readonly IKeyInput _input;
    private readonly IAudioController _audio;

    /// <summary>
    /// Initializes a new instance of the <see cref="VolumeHandler"/> class.
    /// </summary>
    /// <param name="input">The key-synthesis boundary used for step mode.</param>
    /// <param name="audio">The absolute-volume boundary used for set mode.</param>
    public VolumeHandler(IKeyInput input, IAudioController audio)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    }

    /// <inheritdoc />
    public string ActionType => ActionTypes.Volume;

    /// <inheritdoc />
    public Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not VolumeActionConfig volume)
        {
            throw new ArgumentException(
                $"The {nameof(VolumeHandler)} requires a {nameof(VolumeActionConfig)}; got {action.GetType().Name}.",
                nameof(action));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return volume.Mode switch
        {
            VolumeMode.Set => SetLevel(volume.Level),
            VolumeMode.Step => Step(volume.Direction),
            _ => Task.FromResult(ActionExecutionResult.Failed($"Unsupported volume mode '{volume.Mode}'.")),
        };
    }

    private Task<ActionExecutionResult> SetLevel(int level)
    {
        // T-04 clamps configured levels to 0-100; clamp again defensively so the seam contract holds.
        var clamped = Math.Clamp(level, ConfigValidator.MinVolumeLevel, ConfigValidator.MaxVolumeLevel);

        return Task.FromResult(_audio.TrySetVolumeLevel(clamped, out var error)
            ? ActionExecutionResult.Succeeded()
            : ActionExecutionResult.Failed(error ?? "Setting the volume failed."));
    }

    private Task<ActionExecutionResult> Step(VolumeDirection direction)
    {
        if (!TryMapDirection(direction, out var virtualKey))
        {
            // RF-047 fail-fast: an unrecognized direction was disabled at load and executes nothing.
            return Task.FromResult(ActionExecutionResult.Failed($"Unsupported volume step direction '{direction}'."));
        }

        return Task.FromResult(_input.TrySendKey(virtualKey, out var error)
            ? ActionExecutionResult.Succeeded()
            : ActionExecutionResult.Failed(error ?? "Volume step input synthesis failed."));
    }

    /// <summary>Maps a configured step direction to its Win32 virtual key.</summary>
    /// <param name="direction">The configured direction.</param>
    /// <param name="virtualKey">The virtual key when mapping succeeds; 0 otherwise.</param>
    /// <returns><see langword="true"/> when the direction is part of the M1 catalog.</returns>
    internal static bool TryMapDirection(VolumeDirection direction, out ushort virtualKey)
    {
        virtualKey = direction switch
        {
            VolumeDirection.Up => VirtualKeys.VolumeUp,
            VolumeDirection.Down => VirtualKeys.VolumeDown,
            _ => 0,
        };

        return virtualKey != 0;
    }
}
