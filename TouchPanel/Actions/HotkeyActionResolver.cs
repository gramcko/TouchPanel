using TouchPanel.Config;

namespace TouchPanel.Actions;

/// <summary>
/// Default WPF-free <see cref="IHotkeyActionResolver"/> over the live hotkey list (M5-RF-521). The
/// list is supplied through a provider delegate so a widget's hotkey reference always resolves
/// against the current configuration (edits/removals apply without touching the widget).
/// </summary>
/// <remarks>
/// <para><b>First-wins duplicates (M5-RF-523).</b> When several hotkeys share an id, the first in
/// configuration order is resolved, deterministically matching the hotkey subsystem's existing
/// first-wins semantics.</para>
/// <para><b>No chaining.</b> Resolution only returns the referenced hotkey's action; a resolved
/// action that is itself a hotkey reference is rejected at execution time by
/// <see cref="Handlers.RunHotkeyHandler"/> so nothing can recurse.</para>
/// </remarks>
public sealed class HotkeyActionResolver : IHotkeyActionResolver
{
    private readonly Func<IReadOnlyList<HotkeyConfig>> _hotkeysProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyActionResolver"/> class.
    /// </summary>
    /// <param name="hotkeysProvider">Supplies the live hotkey list; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hotkeysProvider"/> is <see langword="null"/>.</exception>
    public HotkeyActionResolver(Func<IReadOnlyList<HotkeyConfig>> hotkeysProvider)
    {
        _hotkeysProvider = hotkeysProvider ?? throw new ArgumentNullException(nameof(hotkeysProvider));
    }

    /// <inheritdoc />
    public bool TryResolveAction(string? hotkeyId, out ActionConfig? action) =>
        TryResolve(_hotkeysProvider(), hotkeyId, out action);

    /// <summary>
    /// Resolves <paramref name="hotkeyId"/> against an explicit hotkey list; shared with validation
    /// (M5-RF-523) so the load-time cross-check and the runtime resolution agree.
    /// </summary>
    /// <param name="hotkeys">The configured hotkeys in configuration order.</param>
    /// <param name="hotkeyId">The referenced hotkey id; blank never resolves.</param>
    /// <param name="action">The first hotkey's bound action when the id exists; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a hotkey with the id exists.</returns>
    public static bool TryResolve(IReadOnlyList<HotkeyConfig>? hotkeys, string? hotkeyId, out ActionConfig? action)
    {
        action = null;
        if (hotkeys is null || string.IsNullOrWhiteSpace(hotkeyId))
        {
            return false;
        }

        var requested = hotkeyId.Trim();
        foreach (var hotkey in hotkeys)
        {
            if (hotkey is not null && string.Equals(hotkey.Id, requested, StringComparison.Ordinal))
            {
                action = hotkey.Action;
                return true;
            }
        }

        return false;
    }
}
