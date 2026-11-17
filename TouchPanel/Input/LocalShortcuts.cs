namespace TouchPanel.Input;

/// <summary>
/// The M2 fixed, application-defined local shortcut commands (RF-060/M2-RF-603). They are inert
/// outside edit mode and are deliberately <b>not</b> user-configurable (the hotkey-management UI is
/// M3/RF-061) and <b>not</b> registered with the operating system; they are handled locally by the
/// panel while edit mode is active, separately from the global <see cref="HotKeyService"/>.
/// </summary>
public enum LocalShortcut
{
    /// <summary>Leaves edit mode (the M1 Esc key).</summary>
    ExitEditMode,

    /// <summary>Nudges the selected widget one DIP (or one grid cell) to the left.</summary>
    NudgeLeft,

    /// <summary>Nudges the selected widget one DIP (or one grid cell) to the right.</summary>
    NudgeRight,

    /// <summary>Nudges the selected widget one DIP (or one grid cell) up.</summary>
    NudgeUp,

    /// <summary>Nudges the selected widget one DIP (or one grid cell) down.</summary>
    NudgeDown,

    /// <summary>Removes the selected widget from the page (M2-RF-603).</summary>
    RemoveSelectedWidget,

    /// <summary>Duplicates the selected widget with a new id and a fixed offset (M2-RF-603).</summary>
    DuplicateSelectedWidget,

    /// <summary>Toggles the edit-mode grid snap for the current session (M2-RF-603).</summary>
    ToggleSnap,

    /// <summary>Restores the previous edit-mode layout state (user-requested undo/redo).</summary>
    Undo,

    /// <summary>Re-applies an undone edit-mode layout state (user-requested undo/redo).</summary>
    Redo,
}

/// <summary>
/// Modifier state of a local shortcut gesture, defined without any WPF/native type so the
/// <c>Input/</c> layer stays WPF-free (M2-RNF-125) and the fixed set is unit-testable in isolation
/// (M2-RNF-124).
/// </summary>
[Flags]
public enum LocalShortcutModifiers
{
    /// <summary>No modifier held (Esc, the arrow keys, Delete).</summary>
    None = 0,

    /// <summary>Ctrl is held (Ctrl+D duplicate, Ctrl+G toggle snap, Ctrl+Z undo, Ctrl+Y redo).</summary>
    Control = 1,

    /// <summary>Shift is held.</summary>
    Shift = 2,

    /// <summary>Alt is held.</summary>
    Alt = 4,

    /// <summary>
    /// The Windows key is held. It is not part of any fixed M2 gesture, so a gesture carrying it
    /// never resolves (strict modifier matching).
    /// </summary>
    Win = 8,
}

/// <summary>
/// The neutral key identity of a local shortcut gesture. A WPF view maps <c>System.Windows.Input.Key</c>
/// onto this enum so the fixed set remains WPF-free and testable.
/// </summary>
public enum LocalShortcutKey
{
    /// <summary>Escape.</summary>
    Escape,

    /// <summary>The left arrow key.</summary>
    Left,

    /// <summary>The right arrow key.</summary>
    Right,

    /// <summary>The up arrow key.</summary>
    Up,

    /// <summary>The down arrow key.</summary>
    Down,

    /// <summary>Delete.</summary>
    Delete,

    /// <summary>The D key (Ctrl+D duplicates).</summary>
    D,

    /// <summary>The G key (Ctrl+G toggles snap).</summary>
    G,

    /// <summary>The Z key (Ctrl+Z undoes an edit-mode change).</summary>
    Z,

    /// <summary>The Y key (Ctrl+Y redoes an undone edit-mode change).</summary>
    Y,
}

/// <summary>
/// Pure resolution of the M2 fixed local shortcut set (RF-060/M2-RF-603, MD-4). It maps a neutral
/// key + modifier gesture onto a <see cref="LocalShortcut"/> and resolves nothing else: no native
/// registration is involved and no global hotkey path is touched (M2-RF-601), so the fixed set can
/// be asserted in unit tests without a UI or the operating system.
/// </summary>
/// <remarks>
/// The fixed set is layered on the M1 keys (Esc exit, arrow nudge) and adds remove (Delete),
/// duplicate (Ctrl+D), toggle snap (Ctrl+G), and undo/redo (Ctrl+Z / Ctrl+Y). All of them are only
/// meaningful while edit mode is active; the caller gates on <c>EditModeService.IsEditMode</c> so
/// they are inert in normal mode (M2-RF-602/M2-RF-604).
/// </remarks>
public static class LocalShortcuts
{
    /// <summary>
    /// Resolves one gesture to its fixed local shortcut command.
    /// </summary>
    /// <param name="key">The pressed key (neutral identity).</param>
    /// <param name="modifiers">The modifier state; modifiers must match the gesture exactly.</param>
    /// <param name="shortcut">The resolved command when the gesture is part of the fixed set; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when the gesture belongs to the fixed M2 local shortcut set.</returns>
    public static bool TryResolve(
        LocalShortcutKey key,
        LocalShortcutModifiers modifiers,
        out LocalShortcut shortcut)
    {
        shortcut = default;

        switch (key)
        {
            case LocalShortcutKey.Escape when modifiers == LocalShortcutModifiers.None:
                shortcut = LocalShortcut.ExitEditMode;
                return true;
            case LocalShortcutKey.Left when modifiers == LocalShortcutModifiers.None:
                shortcut = LocalShortcut.NudgeLeft;
                return true;
            case LocalShortcutKey.Right when modifiers == LocalShortcutModifiers.None:
                shortcut = LocalShortcut.NudgeRight;
                return true;
            case LocalShortcutKey.Up when modifiers == LocalShortcutModifiers.None:
                shortcut = LocalShortcut.NudgeUp;
                return true;
            case LocalShortcutKey.Down when modifiers == LocalShortcutModifiers.None:
                shortcut = LocalShortcut.NudgeDown;
                return true;
            case LocalShortcutKey.Delete when modifiers == LocalShortcutModifiers.None:
                shortcut = LocalShortcut.RemoveSelectedWidget;
                return true;
            case LocalShortcutKey.D when modifiers == LocalShortcutModifiers.Control:
                shortcut = LocalShortcut.DuplicateSelectedWidget;
                return true;
            case LocalShortcutKey.G when modifiers == LocalShortcutModifiers.Control:
                shortcut = LocalShortcut.ToggleSnap;
                return true;
            case LocalShortcutKey.Z when modifiers == LocalShortcutModifiers.Control:
                shortcut = LocalShortcut.Undo;
                return true;
            case LocalShortcutKey.Y when modifiers == LocalShortcutModifiers.Control:
                shortcut = LocalShortcut.Redo;
                return true;
            default:
                return false;
        }
    }
}
