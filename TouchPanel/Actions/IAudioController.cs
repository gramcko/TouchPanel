namespace TouchPanel.Actions;

/// <summary>
/// Testability seam for absolute volume control (RF-048, RF-053). The production implementation
/// lives in <c>Interop/Audio</c> and uses the Core Audio <c>IAudioEndpointVolume</c> interface,
/// because a configured absolute level cannot be reached accurately with volume-key steps (the
/// documented T-09 deviation from DD-8's "all via SendInput"; volume <i>steps</i> and mute stay on
/// the key path).
/// </summary>
public interface IAudioController
{
    /// <summary>
    /// Sets the default audio output device's master volume to an absolute level.
    /// Implementations never throw; failures (no output device, Core Audio unavailable) are
    /// reported as a failed result with a reason (RF-050).
    /// </summary>
    /// <param name="level">The absolute level in percent (0-100); out-of-range values are clamped.</param>
    /// <param name="error">The failure reason when the level was not applied; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the master volume was set.</returns>
    bool TrySetVolumeLevel(int level, out string? error);
}
