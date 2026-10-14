using TouchPanel.Input;

namespace TouchPanel.Tests.Input;

/// <summary>
/// Fixed-set resolution for the M2 edit-mode local shortcuts (RF-060/M2-RF-601..604): every
/// app-defined gesture maps to exactly one command, modifier state must match exactly, and the set
/// is resolved purely so no global registration or UI is involved (M2-RNF-124).
/// </summary>
public sealed class LocalShortcutsTests
{
    [Theory]
    [InlineData(LocalShortcutKey.Escape, LocalShortcutModifiers.None, LocalShortcut.ExitEditMode)]
    [InlineData(LocalShortcutKey.Left, LocalShortcutModifiers.None, LocalShortcut.NudgeLeft)]
    [InlineData(LocalShortcutKey.Right, LocalShortcutModifiers.None, LocalShortcut.NudgeRight)]
    [InlineData(LocalShortcutKey.Up, LocalShortcutModifiers.None, LocalShortcut.NudgeUp)]
    [InlineData(LocalShortcutKey.Down, LocalShortcutModifiers.None, LocalShortcut.NudgeDown)]
    [InlineData(LocalShortcutKey.Delete, LocalShortcutModifiers.None, LocalShortcut.RemoveSelectedWidget)]
    [InlineData(LocalShortcutKey.D, LocalShortcutModifiers.Control, LocalShortcut.DuplicateSelectedWidget)]
    [InlineData(LocalShortcutKey.G, LocalShortcutModifiers.Control, LocalShortcut.ToggleSnap)]
    public void TryResolve_FixedGestures_MapToTheirCommand(
        LocalShortcutKey key,
        LocalShortcutModifiers modifiers,
        LocalShortcut expected)
    {
        var resolved = LocalShortcuts.TryResolve(key, modifiers, out var shortcut);

        Assert.True(resolved);
        Assert.Equal(expected, shortcut);
    }

    [Theory]
    // Ctrl/Alt/Shift/Win are strict: an unexpected modifier makes the gesture not part of the set.
    [InlineData(LocalShortcutKey.Escape, LocalShortcutModifiers.Control)]
    [InlineData(LocalShortcutKey.Left, LocalShortcutModifiers.Shift)]
    [InlineData(LocalShortcutKey.Delete, LocalShortcutModifiers.Alt)]
    [InlineData(LocalShortcutKey.D, LocalShortcutModifiers.None)]
    [InlineData(LocalShortcutKey.D, LocalShortcutModifiers.Control | LocalShortcutModifiers.Shift)]
    [InlineData(LocalShortcutKey.D, LocalShortcutModifiers.Alt)]
    [InlineData(LocalShortcutKey.G, LocalShortcutModifiers.None)]
    [InlineData(LocalShortcutKey.G, LocalShortcutModifiers.Control | LocalShortcutModifiers.Win)]
    public void TryResolve_ModifierMismatch_DoesNotResolve(
        LocalShortcutKey key,
        LocalShortcutModifiers modifiers)
    {
        Assert.False(LocalShortcuts.TryResolve(key, modifiers, out _));
    }

    [Fact]
    public void TryResolve_UnknownKey_DoesNotResolve()
    {
        // A key outside the fixed set (for example A) never resolves, even unmodified.
        Assert.False(LocalShortcuts.TryResolve((LocalShortcutKey)999, LocalShortcutModifiers.None, out _));
    }
}
