namespace TouchPanel.Config;

/// <summary>
/// The page-transition animation a page change plays in normal mode (user-requested addition,
/// additive under schema v4). The enum is the runtime identity used by the animation planner; the
/// configuration stores the string id so an unknown value stays an entry-level warning instead of a
/// file-level error (mirroring the theme/icon-pack ids).
/// </summary>
public enum PageTransitionKind
{
    /// <summary>Slides the new page in from the side (direction-aware for swipes); the default.</summary>
    Slide = 0,

    /// <summary>No animation; the new page appears instantly.</summary>
    None,

    /// <summary>Crossfades the outgoing page into the new one.</summary>
    Fade,

    /// <summary>The new page rises from the bottom edge.</summary>
    SlideUp,

    /// <summary>A subtle scale/opacity settle of the new page.</summary>
    Zoom,
}

/// <summary>
/// Direction of a user page change, used to make the slide transition direction-aware: a swipe/drag
/// to the left (next page) or a tab/Open selection is <see cref="Forward"/>; a swipe to the right
/// (previous page) is <see cref="Backward"/>. Tabs and Open default to forward. Kept in the
/// WPF-free configuration namespace because the page-switch boundary (Services.Config) carries it.
/// </summary>
public enum PageTransitionDirection
{
    /// <summary>The next page (swipe left, tab tap, Open): it enters from the right.</summary>
    Forward = 0,

    /// <summary>The previous page (swipe right): it enters from the left.</summary>
    Backward,
}

/// <summary>
/// The fixed set of page-transition ids (<c>window.pageTransition</c>) and the default. Ids are
/// compared case-insensitively and normalized to their canonical spelling; an unknown or missing id
/// falls back to <see cref="Slide"/>. This type is WPF-free so configuration validation can reuse
/// the same allowlist, and the animation planner can share the same identity.
/// </summary>
public static class PageTransitionModes
{
    /// <summary>No animation.</summary>
    public const string None = "none";

    /// <summary>Direction-aware horizontal slide (default).</summary>
    public const string Slide = "slide";

    /// <summary>Crossfade.</summary>
    public const string Fade = "fade";

    /// <summary>Vertical rise from the bottom edge.</summary>
    public const string SlideUp = "slideUp";

    /// <summary>Subtle scale/opacity settle.</summary>
    public const string Zoom = "zoom";

    /// <summary>The default page-transition id, used when the value is missing or unrecognized.</summary>
    public const string Default = Slide;

    /// <summary>The known page-transition ids, in a stable order for settings surfaces.</summary>
    public static IReadOnlyList<string> All { get; } = [None, Slide, Fade, SlideUp, Zoom];

    private static readonly Dictionary<string, PageTransitionKind> Kinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [None] = PageTransitionKind.None,
            [Slide] = PageTransitionKind.Slide,
            [Fade] = PageTransitionKind.Fade,
            [SlideUp] = PageTransitionKind.SlideUp,
            [Zoom] = PageTransitionKind.Zoom,
        };

    private static readonly Dictionary<string, string> CanonicalIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [None] = None,
            [Slide] = Slide,
            [Fade] = Fade,
            [SlideUp] = SlideUp,
            [Zoom] = Zoom,
        };

    /// <summary>
    /// Whether <paramref name="id"/> names a known page transition (case-insensitive).
    /// </summary>
    /// <param name="id">The candidate id.</param>
    /// <returns><see langword="true"/> when the id is one of the known transitions.</returns>
    public static bool IsKnown(string? id) =>
        !string.IsNullOrWhiteSpace(id) && CanonicalIds.ContainsKey(id.Trim());

    /// <summary>
    /// Normalizes a page-transition id to its canonical spelling, or to <see cref="Default"/> when it
    /// is unknown or missing.
    /// </summary>
    /// <param name="id">The candidate id.</param>
    /// <param name="canonicalId">The canonical id (always a known transition).</param>
    /// <returns><see langword="true"/> when the supplied id was known.</returns>
    public static bool TryNormalize(string? id, out string canonicalId)
    {
        if (!string.IsNullOrWhiteSpace(id) && CanonicalIds.TryGetValue(id.Trim(), out var canonical))
        {
            canonicalId = canonical;
            return true;
        }

        canonicalId = Default;
        return false;
    }

    /// <summary>Normalizes a page-transition id to its canonical id, falling back to slide.</summary>
    /// <param name="id">The candidate id.</param>
    /// <returns>The canonical page-transition id.</returns>
    public static string Normalize(string? id) => TryNormalize(id, out var canonicalId) ? canonicalId : Default;

    /// <summary>
    /// Resolves the runtime identity of a configured id. Unknown values fall back to
    /// <see cref="PageTransitionKind.Slide"/>; the configuration validator reports the warning.
    /// </summary>
    /// <param name="id">The configured id (case-insensitive).</param>
    /// <returns>The transition kind to animate.</returns>
    public static PageTransitionKind Parse(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Kinds.TryGetValue(id.Trim(), out var kind)
            ? kind
            : PageTransitionKind.Slide;
}
