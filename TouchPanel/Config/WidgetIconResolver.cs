namespace TouchPanel.Config;

/// <summary>
/// The renderable form of one resolved icon id: either a font glyph (existing MDL2 entries) or a
/// monochrome vector path (brand and line-art icons). Exactly one of the two is set for a known id.
/// </summary>
/// <param name="Glyph">The MDL2 glyph to render, or <see langword="null"/>.</param>
/// <param name="PathData">The 24x24 vector path to render, or <see langword="null"/>.</param>
/// <param name="Stroke">Whether the vector path is line art drawn with a stroked outline.</param>
/// <param name="StrokeUnits">The stroke pen width in the 24-unit viewbox for a stroked vector.</param>
public readonly record struct IconVisual(
    string? Glyph,
    string? PathData,
    bool Stroke = false,
    double StrokeUnits = 0)
{
    /// <summary>Gets a value indicating whether the resolved visual can be rendered.</summary>
    public bool HasIcon => Glyph is not null || PathData is not null;

    /// <summary>Gets a value indicating whether the visual is a vector path rather than a glyph.</summary>
    public bool IsVector => PathData is not null;
}

/// <summary>
/// Resolves a configured widget icon id to the visual a template renders (user-requested addition).
/// Both the primary <see cref="WidgetConfig.Icon"/> and the label-replacing
/// <see cref="WidgetConfig.LabelIcon"/> go through this single entry point, so the active icon-pack
/// overrides, default-pack fallback, and the glyph-or-vector decision behave identically for both,
/// and the view-model property surface does not have to change again when another id family is
/// introduced.
/// </summary>
public static class WidgetIconResolver
{
    /// <summary>
    /// Resolves one icon id to a glyph or vector path (user-requested brand and line-art icons).
    /// Vector ids are checked first and are <em>pack-independent</em>: those entries have no glyph
    /// and no pack override, so every pack resolves them identically. Glyph ids keep the full pack
    /// behavior: a
    /// pack override wins, an id the pack does not list falls back to the default-pack catalog glyph,
    /// and an unknown/missing id yields <see langword="false"/>.
    /// </summary>
    /// <param name="iconPackId">The active icon-pack id; unknown/missing falls back to the default pack.</param>
    /// <param name="iconId">The stable icon id, or <see langword="null"/> when no icon is configured.</param>
    /// <param name="visual">The resolved glyph or vector path when the id is known.</param>
    /// <returns><see langword="true"/> when a visual was resolved.</returns>
    public static bool TryResolve(string? iconPackId, string? iconId, out IconVisual visual)
    {
        visual = default;
        if (string.IsNullOrWhiteSpace(iconId))
        {
            return false;
        }

        // Brand/line-art vector ids are not part of the glyph-pack namespace; they resolve the same
        // in every pack (user-requested brand and line-art icons).
        if (IconCatalog.TryGet(iconId, out var option) && option.IsVector)
        {
            visual = new IconVisual(
                Glyph: null,
                PathData: option.PathData,
                Stroke: option.Stroke,
                StrokeUnits: option.StrokeUnits);
            return true;
        }

        if (IconPackCatalog.TryGetGlyph(iconPackId, iconId, out var glyph))
        {
            visual = new IconVisual(glyph, PathData: null);
            return true;
        }

        return false;
    }

    /// <summary>Resolves the renderable glyph for an icon id, or <see langword="null"/> when the id is unset/unknown/vector.</summary>
    /// <param name="iconPackId">The active icon-pack id.</param>
    /// <param name="iconId">The stable icon id.</param>
    /// <returns>The resolved glyph, or <see langword="null"/>.</returns>
    public static string? ResolveGlyph(string? iconPackId, string? iconId) =>
        TryResolve(iconPackId, iconId, out var visual) ? visual.Glyph : null;

    /// <summary>Resolves the renderable vector path for an icon id, or <see langword="null"/> when the id is unset/unknown/glyph.</summary>
    /// <param name="iconPackId">The active icon-pack id; vector ids are pack-independent.</param>
    /// <param name="iconId">The stable icon id.</param>
    /// <returns>The resolved 24x24 path data, or <see langword="null"/>.</returns>
    public static string? ResolvePathData(string? iconPackId, string? iconId) =>
        TryResolve(iconPackId, iconId, out var visual) ? visual.PathData : null;

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
}
