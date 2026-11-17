namespace TouchPanel.Theme;

/// <summary>
/// The default appearance values for one built-in theme (M5-RF-540): a foreground, a background, an
/// accent, a font size, and an opacity. Colors are <c>#RRGGBB</c>/<c>#AARRGGBB</c> strings so the
/// table stays plain, WPF-free data.
/// </summary>
/// <param name="Foreground">The inherited widget foreground color.</param>
/// <param name="Background">The inherited widget background color.</param>
/// <param name="Accent">The theme accent color.</param>
/// <param name="FontSize">The default widget font size in DIPs.</param>
/// <param name="Opacity">The default widget opacity in <c>[0,1]</c>.</param>
public sealed record ThemeAppearanceDefault(
    string Foreground,
    string Background,
    string Accent,
    double FontSize,
    double Opacity);

/// <summary>
/// WPF-free per-theme appearance-default table keyed by the canonical <see cref="ThemeIds"/>
/// (M5-RF-540). The dark entry reproduces the delivered dark palette so applying the default is a
/// no-op; light and high-contrast provide readable, touch-legible values (high contrast favors
/// larger type). This is the single source of truth for the editor's inherited values and for
/// seeding new widgets (M5-RF-541/M5-RF-542).
/// </summary>
/// <remarks>
/// Per Q-M5-1 the table is used to seed only the theme-independent fields (font size, opacity) plus
/// the accent where a widget uses it; foreground/background stay inherited so a later theme switch
/// keeps restyling the widget. All values are within <see cref="TouchPanel.Config.WidgetAppearance"/>
/// bounds and validated/normalized at the point of use.
/// </remarks>
public static class ThemeAppearanceDefaults
{
    /// <summary>The dark defaults; these reproduce the delivered Core-Release palette exactly.</summary>
    public static ThemeAppearanceDefault Dark { get; } = new(
        Foreground: "#EDEDED",
        Background: "#2A2A30",
        Accent: "#3D7EFF",
        FontSize: 15,
        Opacity: 1.0);

    /// <summary>The light defaults: a dark foreground on a white surface with a deep accent.</summary>
    public static ThemeAppearanceDefault Light { get; } = new(
        Foreground: "#1B1B1F",
        Background: "#FFFFFF",
        Accent: "#1D5FE0",
        FontSize: 15,
        Opacity: 1.0);

    /// <summary>The high-contrast defaults: white on black with a strong yellow accent and larger type.</summary>
    public static ThemeAppearanceDefault HighContrast { get; } = new(
        Foreground: "#FFFFFF",
        Background: "#0A0A0A",
        Accent: "#FFFF00",
        FontSize: 18,
        Opacity: 1.0);

    /// <summary>The midnight defaults: a pale blue-white foreground on the deep-blue surface.</summary>
    public static ThemeAppearanceDefault Midnight { get; } = new(
        Foreground: "#E8ECF8",
        Background: "#161E33",
        Accent: "#6FA8FF",
        FontSize: 15,
        Opacity: 1.0);

    /// <summary>The nord defaults: a warm off-white foreground on the blue-grey surface.</summary>
    public static ThemeAppearanceDefault Nord { get; } = new(
        Foreground: "#ECEFF4",
        Background: "#3B4252",
        Accent: "#88C0D0",
        FontSize: 15,
        Opacity: 1.0);

    /// <summary>The oled defaults: light text on a near-black surface with a cyan accent.</summary>
    public static ThemeAppearanceDefault Oled { get; } = new(
        Foreground: "#F2F2F2",
        Background: "#121212",
        Accent: "#00C2FF",
        FontSize: 15,
        Opacity: 1.0);

    /// <summary>
    /// Gets the defaults for a theme id; an unknown or missing id falls back to <see cref="Dark"/>
    /// (M5-RF-540 edge case). Never throws and never returns <see langword="null"/>.
    /// </summary>
    /// <param name="themeId">The active theme id; may be <see langword="null"/>.</param>
    /// <returns>The theme's appearance defaults.</returns>
    public static ThemeAppearanceDefault For(string? themeId) => ThemeIds.Normalize(themeId) switch
    {
        ThemeIds.Light => Light,
        ThemeIds.HighContrast => HighContrast,
        ThemeIds.Midnight => Midnight,
        ThemeIds.Nord => Nord,
        ThemeIds.Oled => Oled,
        _ => Dark,
    };

    /// <summary>
    /// Attempts to get the defaults for a theme id. Provided for parity with the other theme
    /// lookups; the lookup always succeeds by falling back to <see cref="Dark"/>.
    /// </summary>
    /// <param name="themeId">The active theme id; may be <see langword="null"/>.</param>
    /// <param name="value">The resolved defaults.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool TryGet(string? themeId, out ThemeAppearanceDefault value)
    {
        value = For(themeId);
        return true;
    }
}
