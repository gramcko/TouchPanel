namespace TouchPanel.Config;

/// <summary>
/// Resolves a configured widget icon id to the glyph a template renders (user-requested addition).
/// Both the primary <see cref="WidgetConfig.Icon"/> and the label-replacing
/// <see cref="WidgetConfig.LabelIcon"/> go through this single entry point, so the active icon-pack
/// overrides and default-pack fallback behave identically for both, and the view-model property
/// surface does not have to change again when another id family is introduced (for example a future
/// vector/brand icon set: its ids would be recognized here and mapped to glyph data).
/// </summary>
public static class WidgetIconResolver
{
    /// <summary>
    /// Resolves the renderable glyph for a stable icon id through the active icon pack
    /// (M3-RF-844/M3-RF-845): a pack override wins, an id the pack does not list falls back to the
    /// default-pack catalog glyph, and an unknown/missing id yields <see langword="false"/>.
    /// </summary>
    /// <param name="iconPackId">The active icon-pack id; unknown/missing falls back to the default pack.</param>
    /// <param name="iconId">The stable icon id, or <see langword="null"/> when no icon is configured.</param>
    /// <param name="glyph">The resolved glyph when the id is known.</param>
    /// <returns><see langword="true"/> when a glyph was resolved.</returns>
    public static bool TryGetGlyph(string? iconPackId, string? iconId, out string glyph) =>
        IconPackCatalog.TryGetGlyph(iconPackId, iconId, out glyph);

    /// <summary>
    /// Resolves the renderable glyph for a stable icon id through the active pack, or
    /// <see langword="null"/> when the id is unset/unknown.
    /// </summary>
    /// <param name="iconPackId">The active icon-pack id.</param>
    /// <param name="iconId">The stable icon id.</param>
    /// <returns>The resolved glyph, or <see langword="null"/>.</returns>
    public static string? ResolveGlyph(string? iconPackId, string? iconId) =>
        TryGetGlyph(iconPackId, iconId, out var glyph) ? glyph : null;
}
