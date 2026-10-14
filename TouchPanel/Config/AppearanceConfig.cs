using TouchPanel.Theme;

namespace TouchPanel.Config;

/// <summary>
/// Global appearance configuration (M3-RF-841, schema v4): the active built-in theme id, an optional
/// global accent color, and the active built-in icon-pack id. All three are plain data; the model
/// never interprets them. Validation normalizes unknown ids and invalid colors (M3-RF-847), and the
/// appearance apply pipeline resolves them (T-09/T-10).
/// </summary>
public sealed class AppearanceConfig
{
    private string _theme = ThemeIds.Dark;
    private string _iconPack = IconPackIds.Default;

    /// <summary>
    /// Gets or sets the built-in theme id (M3-RF-842); defaults to <see cref="ThemeIds.Dark"/>. A
    /// missing or explicit JSON null is normalized to the default; an unknown id is normalized by
    /// validation with a warning (M3-RF-847).
    /// </summary>
    public string Theme
    {
        get => _theme;
        set => _theme = value ?? ThemeIds.Dark;
    }

    /// <summary>
    /// Gets or sets the global accent color as <c>#RRGGBB</c>/<c>#AARRGGBB</c>, or
    /// <see langword="null"/> to use the active theme's accent (M3-RF-843). Validation normalizes a
    /// valid color to uppercase and clears an invalid/blank value to <see langword="null"/> with a
    /// warning (M3-RF-847).
    /// </summary>
    public string? AccentColor { get; set; }

    /// <summary>
    /// Gets or sets the built-in icon-pack id (M3-RF-844); defaults to
    /// <see cref="IconPackIds.Default"/>. A missing or explicit JSON null is normalized to the
    /// default; an unknown id is normalized by validation with a warning (M3-RF-847).
    /// </summary>
    public string IconPack
    {
        get => _iconPack;
        set => _iconPack = value ?? IconPackIds.Default;
    }
}
