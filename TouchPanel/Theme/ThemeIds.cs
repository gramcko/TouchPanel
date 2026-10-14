namespace TouchPanel.Theme;

/// <summary>
/// The fixed set of built-in theme ids (M3-RF-842) and their compiled resource locations. Ids are
/// compared case-insensitively and normalized to their canonical spelling (M3-RF-847); unknown or
/// missing ids fall back to <see cref="Dark"/>. This type is WPF-free so configuration validation
/// (T-08) can reuse the same allowlist.
/// </summary>
public static class ThemeIds
{
    /// <summary>The default theme id; reproduces the Core-Release palette exactly.</summary>
    public const string Dark = "dark";

    /// <summary>The light built-in theme id.</summary>
    public const string Light = "light";

    /// <summary>The high-contrast built-in theme id.</summary>
    public const string HighContrast = "highContrast";

    /// <summary>
    /// The resource key every built-in theme dictionary uses to advertise its stable theme id.
    /// <see cref="ThemeManager"/> uses it to identify the currently merged application-level theme
    /// dictionary; templates never reference it.
    /// </summary>
    public const string ThemeIdKey = "ThemeId";

    /// <summary>The known theme ids, in a stable order (default first).</summary>
    public static IReadOnlyList<string> All { get; } = [Dark, Light, HighContrast];

    private static readonly Dictionary<string, ThemeDescriptor> Descriptors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Dark] = new(Dark, "Theme/Themes/Dark.xaml"),
            [Light] = new(Light, "Theme/Themes/Light.xaml"),
            [HighContrast] = new(HighContrast, "Theme/Themes/HighContrast.xaml"),
        };

    /// <summary>
    /// Whether <paramref name="themeId"/> names a built-in theme (case-insensitive).
    /// </summary>
    /// <param name="themeId">The candidate id.</param>
    /// <returns><see langword="true"/> when the id is one of the built-in themes.</returns>
    public static bool IsKnown(string? themeId) =>
        !string.IsNullOrWhiteSpace(themeId) && Descriptors.ContainsKey(themeId.Trim());

    /// <summary>
    /// Normalizes a theme id to its canonical spelling, or to <see cref="Dark"/> when it is unknown
    /// or missing (M3-RF-847).
    /// </summary>
    /// <param name="themeId">The candidate id.</param>
    /// <param name="canonicalId">The canonical built-in id (always a valid theme id).</param>
    /// <returns><see langword="true"/> when the supplied id was a known theme.</returns>
    public static bool TryNormalize(string? themeId, out string canonicalId)
    {
        if (!string.IsNullOrWhiteSpace(themeId) && Descriptors.TryGetValue(themeId.Trim(), out var descriptor))
        {
            canonicalId = descriptor.Id;
            return true;
        }

        canonicalId = Dark;
        return false;
    }

    /// <summary>Normalizes a theme id to its canonical built-in id, falling back to dark.</summary>
    /// <param name="themeId">The candidate id.</param>
    /// <returns>The canonical built-in theme id.</returns>
    public static string Normalize(string? themeId) => TryNormalize(themeId, out var canonicalId) ? canonicalId : Dark;

    /// <summary>
    /// Resolves the compiled resource URI of a built-in theme dictionary.
    /// </summary>
    /// <param name="themeId">A built-in theme id (case-insensitive).</param>
    /// <param name="uri">The relative pack URI of the theme dictionary.</param>
    /// <returns><see langword="true"/> when the id is a built-in theme.</returns>
    public static bool TryGetResourceUri(string themeId, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(themeId) && Descriptors.TryGetValue(themeId.Trim(), out var descriptor))
        {
            uri = new Uri("/TouchPanel;component/" + descriptor.RelativePath, UriKind.Relative);
            return true;
        }

        uri = null!;
        return false;
    }

    private readonly record struct ThemeDescriptor(string Id, string RelativePath);
}
