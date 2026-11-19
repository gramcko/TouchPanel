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

    /// <summary>
    /// The alternate <c>outline</c> pack: line-art/compact alternatives from the same
    /// "Segoe MDL2 Assets" font for a broad subset of the stable ids. Ids not listed fall back to the
    /// default pack, so the pack never clears an icon.
    /// </summary>
    private static readonly Dictionary<string, string> OutlineGlyphs = new(StringComparer.Ordinal)
    {
        ["power"] = "\uE945",
        ["volume"] = "\uE993",
        ["mute"] = "\uE74F",
        ["settings"] = "\uE912",
        ["home"] = "\uE8FC",
        ["search"] = "\uE773",
        ["star"] = "\uE728",
        ["heart"] = "\uE8D9",
        ["camera"] = "\uE8B8",
        ["mail"] = "\uE8C3",
        ["music"] = "\uE90B",
        ["video"] = "\uE8AA",
        ["folder"] = "\uE838",
        ["document"] = "\uE7C3",
        ["globe"] = "\uE909",
        ["refresh"] = "\uE895",
        ["add"] = "\uE948",
        ["remove"] = "\uE949",
        ["check"] = "\uE739",
        ["cancel"] = "\uE894",
        ["edit"] = "\uE8AC",
        ["delete"] = "\uE75C",
        ["save"] = "\uE78C",
        ["share"] = "\uE8EB",
        ["clock"] = "\uE916",
        ["calendar"] = "\uE8BF",
        ["bell"] = "\uE7ED",
        ["wifi"] = "\uE872",
        ["bluetooth"] = "\uE703",
        ["battery"] = "\uE850",
        ["cpu"] = "\uE957",
        ["monitor"] = "\uE7F8",
        ["keyboard"] = "\uE92E",
        ["mouse"] = "\uE961",
        ["game"] = "\uE967",
        ["map"] = "\uE81E",
    };

    /// <summary>
    /// The alternate <c>duotone</c> pack: emphasis-filled alternatives for the ids that have a
    /// natural filled/level variant in "Segoe MDL2 Assets". Ids not listed fall back to the default
    /// pack.
    /// </summary>
    private static readonly Dictionary<string, string> DuotoneGlyphs = new(StringComparer.Ordinal)
    {
        ["star"] = "\uE735",
        ["heart"] = "\uEB52",
        ["check"] = "\uE73A",
        ["mail"] = "\uE8A8",
        ["folder"] = "\uE8D5",
        ["document"] = "\uE729",
        ["camera"] = "\uE91B",
        ["calendar"] = "\uE8C0",
        ["battery"] = "\uE859",
        ["volume"] = "\uE995",
        ["wifi"] = "\uE874",
        ["game"] = "\uE990",
        ["monitor"] = "\uE977",
        ["mouse"] = "\uE815",
        ["map"] = "\uE81D",
        ["save"] = "\uE792",
        ["music"] = "\uE93C",
        ["video"] = "\uE8B2",
        ["settings"] = "\uE7F3",
        ["clock"] = "\uE81C",
    };

    /// <summary>Glyph overrides per built-in pack; the default pack has no overrides by definition.</summary>
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Overrides =
        new(StringComparer.Ordinal)
        {
            [IconPackIds.Filled] = FilledGlyphs,
            [IconPackIds.Outline] = OutlineGlyphs,
            [IconPackIds.Duotone] = DuotoneGlyphs,
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
        if (Overrides.TryGetValue(pack, out var overrides)
            && overrides.TryGetValue(iconId, out var alternate))
        {
            glyph = alternate;
            return true;
        }

        if (IconCatalog.TryGet(iconId, out var option) && option.HasGlyph)
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
