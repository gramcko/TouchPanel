namespace TouchPanel.Config;

/// <summary>
/// The fixed set of built-in icon-pack ids (M3-RF-844) and the default pack. Ids are compared
/// case-insensitively and normalized to their canonical spelling (M3-RF-847); an unknown or missing
/// id falls back to <see cref="Default"/>. This type is WPF-free so configuration validation (T-08)
/// can reuse the same allowlist; glyph resolution for each pack is added by the icon-pack catalog
/// (T-09), which shares this one stable id namespace.
/// </summary>
public static class IconPackIds
{
    /// <summary>The default built-in pack id; unchanged from the M2 icon catalog.</summary>
    public const string Default = "default";

    /// <summary>The alternate built-in pack id (a filled variant of the default glyphs).</summary>
    public const string Filled = "filled";

    /// <summary>The built-in pack id for line-art/alternate glyph variants.</summary>
    public const string Outline = "outline";

    /// <summary>The built-in pack id for emphasis-filled glyph variants.</summary>
    public const string Duotone = "duotone";

    /// <summary>The known icon-pack ids, in a stable order (default first).</summary>
    public static IReadOnlyList<string> All { get; } = [Default, Filled, Outline, Duotone];

    private static readonly Dictionary<string, string> CanonicalIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Default] = Default,
            [Filled] = Filled,
            [Outline] = Outline,
            [Duotone] = Duotone,
        };

    /// <summary>
    /// Whether <paramref name="iconPackId"/> names a built-in icon pack (case-insensitive).
    /// </summary>
    /// <param name="iconPackId">The candidate id.</param>
    /// <returns><see langword="true"/> when the id is one of the built-in packs.</returns>
    public static bool IsKnown(string? iconPackId) =>
        !string.IsNullOrWhiteSpace(iconPackId) && CanonicalIds.ContainsKey(iconPackId.Trim());

    /// <summary>
    /// Normalizes an icon-pack id to its canonical spelling, or to <see cref="Default"/> when it is
    /// unknown or missing (M3-RF-847).
    /// </summary>
    /// <param name="iconPackId">The candidate id.</param>
    /// <param name="canonicalId">The canonical built-in id (always a valid pack id).</param>
    /// <returns><see langword="true"/> when the supplied id was a known pack.</returns>
    public static bool TryNormalize(string? iconPackId, out string canonicalId)
    {
        if (!string.IsNullOrWhiteSpace(iconPackId) && CanonicalIds.TryGetValue(iconPackId.Trim(), out var canonical))
        {
            canonicalId = canonical;
            return true;
        }

        canonicalId = Default;
        return false;
    }

    /// <summary>Normalizes an icon-pack id to its canonical built-in id, falling back to default.</summary>
    /// <param name="iconPackId">The candidate id.</param>
    /// <returns>The canonical built-in icon-pack id.</returns>
    public static string Normalize(string? iconPackId) =>
        TryNormalize(iconPackId, out var canonicalId) ? canonicalId : Default;
}
