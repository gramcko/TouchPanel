namespace TouchPanel.Config;

/// <summary>
/// The built-in icon-pack catalog (M3-RF-844). Packs share one stable icon-id namespace: a pack maps
/// a <em>subset</em> of ids to alternate glyphs, and any id absent from the active pack resolves to
/// the <see cref="IconPackIds.Default"/> pack glyph, so ids are never cleared by the active pack and
/// a per-widget <c>icon</c> stays pack-independent (no per-widget migration, F-M3-05). External or
/// loaded packs are out of scope (M3-RNF-135). This type is WPF-free and only resolves inert glyph
/// data.
/// </summary>
public static class IconPackCatalog
{
    /// <summary>
    /// The alternate <c>filled</c> pack: a subset of the default ids mapped to their filled glyphs in
    /// the bundled "Segoe MDL2 Assets" font. Every id not listed here falls back to the default pack.
    /// </summary>
    private static readonly Dictionary<string, string> FilledGlyphs = new(StringComparer.Ordinal)
    {
        ["star"] = "\uE735",
        ["heart"] = "\uEB52",
        ["bell"] = "\uE7E7",
        ["check"] = "\uE73E",
        ["add"] = "\uE710",
        ["play"] = "\uE768",
        ["pause"] = "\uE769",
        ["stop"] = "\uE71A",
        ["power"] = "\uE7E8",
    };

    /// <summary>Gets the known built-in pack ids, in a stable order (default first).</summary>
    public static IReadOnlyList<string> PackIds => IconPackIds.All;

    /// <summary>
    /// Resolves the glyph for an icon id through the active pack (M3-RF-844): an id mapped by the
    /// active pack wins; otherwise the <see cref="IconPackIds.Default"/> pack glyph is used. An
    /// unknown/missing id yields <see langword="false"/>.
    /// </summary>
    /// <param name="iconPackId">The active icon-pack id (case-insensitive; unknown falls back to default).</param>
    /// <param name="iconId">The stable per-widget icon id, or <see langword="null"/> when no icon is configured.</param>
    /// <param name="glyph">The resolved glyph when the id is known.</param>
    /// <returns><see langword="true"/> when a glyph was resolved.</returns>
    public static bool TryGetGlyph(string? iconPackId, string? iconId, out string glyph)
    {
        glyph = string.Empty;
        if (string.IsNullOrWhiteSpace(iconId))
        {
            return false;
        }

        var pack = IconPackIds.Normalize(iconPackId);
        if (string.Equals(pack, IconPackIds.Filled, StringComparison.Ordinal)
            && FilledGlyphs.TryGetValue(iconId, out var filled))
        {
            glyph = filled;
            return true;
        }

        if (IconCatalog.TryGet(iconId, out var option))
        {
            glyph = option.Glyph;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves the glyph for an icon id through the active pack, or <see langword="null"/> when the
    /// id is unset/unknown (M3-RF-844/M3-RF-845).
    /// </summary>
    /// <param name="iconPackId">The active icon-pack id.</param>
    /// <param name="iconId">The stable per-widget icon id.</param>
    /// <returns>The resolved glyph, or <see langword="null"/>.</returns>
    public static string? GlyphFor(string? iconPackId, string? iconId) =>
        TryGetGlyph(iconPackId, iconId, out var glyph) ? glyph : null;
}
