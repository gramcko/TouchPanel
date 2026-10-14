using TouchPanel.Config;

namespace TouchPanel.Input;

/// <summary>
/// One validated global hotkey ready to register (RF-058, M2-RF-581): the numeric registration id
/// reported by <c>WM_HOTKEY</c>, the resolved modifier flags and virtual key, a display string, and
/// the single bound action dispatched through <see cref="Actions.IActionExecutor"/>.
/// </summary>
public sealed class HotKeyBinding
{
    internal HotKeyBinding(
        int registrationId,
        string configId,
        string keyName,
        ushort virtualKey,
        HotKeyModifiers modifiers,
        string combination,
        ActionConfig action)
    {
        RegistrationId = registrationId;
        ConfigId = configId;
        KeyName = keyName;
        VirtualKey = virtualKey;
        Modifiers = modifiers;
        Combination = combination;
        Action = action;
    }

    /// <summary>Gets the unique numeric registration id (<c>WM_HOTKEY</c> <c>wParam</c>).</summary>
    public int RegistrationId { get; }

    /// <summary>Gets the configuration entry id, used for structured logging (may be synthesized when absent).</summary>
    public string ConfigId { get; }

    /// <summary>Gets the friendly key name (for example <c>A</c> or <c>F5</c>).</summary>
    public string KeyName { get; }

    /// <summary>Gets the Win32 virtual-key code of the target key.</summary>
    public ushort VirtualKey { get; }

    /// <summary>
    /// Gets the registered modifier flags, including <see cref="HotKeyModifiers.NoRepeat"/>; at
    /// least one real modifier is always present (D-M2-4).
    /// </summary>
    public HotKeyModifiers Modifiers { get; }

    /// <summary>Gets the human-readable combination in canonical modifier order (for example <c>Ctrl+Alt+A</c>).</summary>
    public string Combination { get; }

    /// <summary>Gets the single action bound to this hotkey (RF-058, M2-RF-581).</summary>
    public ActionConfig Action { get; }

    /// <summary>
    /// Gets a deterministic identity used to detect duplicate combinations within one configuration
    /// (M2-RF-593): modifier flags and target key only, independent of name casing or edit order.
    /// </summary>
    public string Identity => $"{(uint)Modifiers}:{VirtualKey}";
}
