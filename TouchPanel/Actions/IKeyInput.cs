namespace TouchPanel.Actions;

/// <summary>
/// Testability seam for key synthesis (RF-053): action handlers map semantic actions (media keys,
/// volume steps) to Win32 virtual keys and call this boundary, while the only <c>SendInput</c>
/// P/Invoke lives under <c>Interop/Input</c> (RNF-010). Implementations never throw; injection
/// failures are reported as a failed result with a reason (RF-050/F-14).
/// </summary>
public interface IKeyInput
{
    /// <summary>Sends one virtual-key press (key down followed by key up) to the system.</summary>
    /// <param name="virtualKey">The Win32 virtual-key code.</param>
    /// <param name="error">The failure reason when the input was not injected; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the key press was injected.</returns>
    bool TrySendKey(ushort virtualKey, out string? error);

    /// <summary>
    /// Holds one virtual key down without releasing it. Used by the send-keys handler to press
    /// modifiers before the target key (RF-057, M2-RF-572); the matching <see cref="TryKeyUp"/> is
    /// always issued so no modifier is left stuck.
    /// </summary>
    /// <param name="virtualKey">The Win32 virtual-key code.</param>
    /// <param name="error">The failure reason when the input was not injected; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the key-down event was injected.</returns>
    bool TryKeyDown(ushort virtualKey, out string? error);

    /// <summary>Releases a virtual key previously held down by <see cref="TryKeyDown"/>.</summary>
    /// <param name="virtualKey">The Win32 virtual-key code.</param>
    /// <param name="error">The failure reason when the release was not injected; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the key-up event was injected.</returns>
    bool TryKeyUp(ushort virtualKey, out string? error);
}
