using TouchPanel.Config;

namespace TouchPanel.Services.Appearance;

/// <summary>
/// Makes an appearance apply atomic across the three appearance facets (theme, accent, icon pack)
/// (M3-RF-848/F-M3-16). The theme/accent swap owns its own wholesale rollback; this unit ensures the
/// icon pack is published to the shared <see cref="AppearanceState"/> only when that swap succeeded,
/// so a failed theme/accent apply never leaves a partially-applied appearance (old theme/accent with
/// the new pack). It is WPF-free so the decision is unit-testable in isolation.
/// </summary>
public sealed class AppearanceApplier
{
    private readonly Func<string?, string?, bool> _applyThemeAndAccent;
    private readonly AppearanceState? _appearanceState;

    /// <summary>
    /// Creates an applier over a theme/accent apply delegate and the shared appearance state.
    /// </summary>
    /// <param name="applyThemeAndAccent">
    /// Applies the requested theme + accent and returns <see langword="true"/> when the requested
    /// (normalized) appearance is active afterwards; the implementation is expected to restore the
    /// previous theme/accent wholesale and return <see langword="false"/> on failure.
    /// </param>
    /// <param name="appearanceState">
    /// The shared icon-pack state to publish to on success, or <see langword="null"/> when no consumer
    /// is wired (the theme/accent swap still applies).
    /// </param>
    public AppearanceApplier(
        Func<string?, string?, bool> applyThemeAndAccent,
        AppearanceState? appearanceState)
    {
        _applyThemeAndAccent = applyThemeAndAccent ?? throw new ArgumentNullException(nameof(applyThemeAndAccent));
        _appearanceState = appearanceState;
    }

    /// <summary>
    /// Applies the theme and accent, then publishes the icon pack to the shared state only when the
    /// theme/accent apply succeeded (M3-RF-848/F-M3-16). On failure the pack is left untouched, so the
    /// previously active appearance remains in effect wholesale.
    /// </summary>
    /// <param name="appearance">The validated appearance section.</param>
    /// <returns>
    /// <see langword="true"/> when the appearance was applied and the pack published;
    /// <see langword="false"/> when the theme/accent apply failed and nothing was published.
    /// </returns>
    public bool Apply(AppearanceConfig appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (!_applyThemeAndAccent(appearance.Theme, appearance.AccentColor))
        {
            return false;
        }

        _appearanceState?.SetAppearance(appearance);
        return true;
    }
}
