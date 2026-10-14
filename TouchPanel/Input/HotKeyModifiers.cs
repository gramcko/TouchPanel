namespace TouchPanel.Input;

/// <summary>
/// Win32 modifier flags for a global hotkey (RF-058, M2-RF-582), defined here so the
/// <c>Input/</c> layer and its tests stay free of native types (M2-RNF-125). The numeric values
/// match the <c>MOD_*</c> constants accepted by <c>RegisterHotKey</c>; the <c>Interop/</c> wrapper
/// passes them through unchanged.
/// </summary>
[Flags]
public enum HotKeyModifiers : uint
{
    /// <summary>No modifier. A hotkey with this flag alone is invalid (M2-RF-594/D-M2-4).</summary>
    None = 0,

    /// <summary><c>MOD_ALT</c>.</summary>
    Alt = 0x0001,

    /// <summary><c>MOD_CONTROL</c>.</summary>
    Control = 0x0002,

    /// <summary><c>MOD_SHIFT</c>.</summary>
    Shift = 0x0004,

    /// <summary><c>MOD_WIN</c>.</summary>
    Win = 0x0008,

    /// <summary>
    /// <c>MOD_NOREPEAT</c>: holding the combination fires the action once instead of auto-repeating.
    /// Held-key repeat is not part of M2 (RF-042 was dropped), so every registered hotkey sets this.
    /// </summary>
    NoRepeat = 0x4000,
}
