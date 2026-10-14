namespace TouchPanel.Input;

/// <summary>
/// The boundary that isolates <see cref="HotKeyService"/> from Win32 hotkey registration
/// (plan section 1.3, M2-RF-582, M2-RNF-124). The production implementation lives under
/// <c>Interop/</c> and owns the hidden UI-thread message window; tests substitute a fake so no real
/// system hotkey is ever captured.
/// </summary>
/// <remarks>
/// Every member is thread-affine to the UI thread in the production implementation
/// (<c>RegisterHotKey</c>/<c>UnregisterHotKey</c> and the hidden window). <see cref="HotKeyService"/>
/// calls them only from the UI thread via its injected marshaller (M2-RF-582/MD-16).
/// </remarks>
public interface IHotKeyRegistration : IDisposable
{
    /// <summary>
    /// Occurs when a registered hotkey is pressed. Raised on the UI thread from the hidden window's
    /// message pump; the handler must never block (M2-RF-583).
    /// </summary>
    event EventHandler<HotKeyPressedEventArgs>? Pressed;

    /// <summary>
    /// Registers one hotkey combination with the operating system.
    /// </summary>
    /// <param name="id">The unique numeric registration id (delivered back as the pressed event id).</param>
    /// <param name="modifiers">The modifier flags; at least one real modifier is enforced upstream.</param>
    /// <param name="virtualKey">The Win32 virtual-key code of the target key.</param>
    /// <returns><see langword="true"/> when the combination was registered; <see langword="false"/> when the OS refused it (for example it is already taken).</returns>
    bool TryRegister(int id, HotKeyModifiers modifiers, ushort virtualKey);

    /// <summary>
    /// Unregisters one previously registered combination. Idempotent for ids that are not registered.
    /// </summary>
    /// <param name="id">The numeric registration id to release.</param>
    /// <returns><see langword="true"/> when the OS released the registration.</returns>
    bool TryUnregister(int id);
}
