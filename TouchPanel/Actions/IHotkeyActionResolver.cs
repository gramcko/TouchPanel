using TouchPanel.Config;

namespace TouchPanel.Actions;

/// <summary>
/// Narrow, injectable seam that resolves a hotkey-reference action's target (M5-RF-521). It reads
/// the live <c>PanelConfig.Hotkeys</c> so editing or removing a hotkey applies to any widget that
/// references it without touching the widget, and so the reference stays unit-testable without the
/// OS (M5-RNF-551). WPF-free (M5-RNF-552).
/// </summary>
public interface IHotkeyActionResolver
{
    /// <summary>
    /// Resolves the action bound to the hotkey with the given id.
    /// </summary>
    /// <param name="hotkeyId">The referenced hotkey id; blank never resolves.</param>
    /// <param name="action">
    /// The first hotkey's bound action when an entry with the id exists (it may be
    /// <see langword="null"/> when the hotkey has no usable action); otherwise <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when a hotkey with the id exists.</returns>
    bool TryResolveAction(string? hotkeyId, out ActionConfig? action);
}
