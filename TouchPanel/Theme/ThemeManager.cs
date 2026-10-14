using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Serilog;
using TouchPanel.Config;

namespace TouchPanel.Theme;

/// <summary>
/// Applies a built-in theme and the global accent by swapping the ACTIVE theme
/// <see cref="ResourceDictionary"/> in the application-level merged dictionaries and layering the
/// accent override on top (M3-RF-843/M3-RF-845/M3-RF-848/M3-RF-851). This is the WPF-affine host of
/// the theming layer; it keeps no business logic and runs only on the UI thread (callers marshal to
/// the dispatcher, M3-RNF-132).
/// <para>
/// Guarantees:
/// <list type="bullet">
/// <item>An unknown or missing theme id falls back to <see cref="ThemeIds.Dark"/> with a logged
/// warning (M3-RF-847).</item>
/// <item>An invalid/blank accent falls back to the theme accent (<see langword="null"/>) with a
/// warning (M3-RF-843/M3-RF-847); a valid accent overrides <c>PanelAccentBrush</c> app-wide and
/// layers under the per-widget accent override, which templates apply on top.</item>
/// <item>Applying is idempotent: re-applying the active theme and accent is a no-op.</item>
/// <item>Swap-with-wholesale-rollback: if loading/applying the new theme or accent fails, the
/// previous merged-dictionary + accent state is restored in full, so the UI is never left with a mix
/// of old and new palette keys (M3-RF-848/F-M3-16); the failure is logged and isolated.</item>
/// <item>Theme changes are announced through <see cref="ThemeChanged"/> — no polling.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ThemeManager
{
    private const string AccentBrushKey = "PanelAccentBrush";

    private readonly ILogger _logger;
    private readonly ResourceDictionary _applicationResources;
    private readonly Func<string, ResourceDictionary?> _themeLoader;
    private readonly Action<ResourceDictionary, ResourceDictionary, ResourceDictionary?> _swap;
    private readonly Action<ResourceDictionary, string?> _accentApplier;

    private ResourceDictionary? _activeThemeDictionary;
    private string _currentThemeId = ThemeIds.Dark;
    private string? _currentAccent;

    /// <summary>
    /// Creates a manager over the current WPF application's resources. The active theme dictionary
    /// merged by <c>App.xaml</c> is adopted as the starting theme.
    /// </summary>
    /// <param name="logger">The logger used for fallback and failure diagnostics.</param>
    public ThemeManager(ILogger logger)
        : this(logger, GetApplicationResources(), themeLoader: null, swap: null, accentApplier: null)
    {
    }

    /// <summary>
    /// Test/DI seam: creates a manager over an explicit application-resources dictionary with
    /// optional theme loader, swap, and accent-applier injection.
    /// </summary>
    /// <param name="logger">The logger used for fallback and failure diagnostics.</param>
    /// <param name="applicationResources">The dictionary whose <see cref="ResourceDictionary.MergedDictionaries"/> are swapped.</param>
    /// <param name="themeLoader">Loads a built-in theme dictionary; defaults to compiled BAML loading.</param>
    /// <param name="swap">Performs the merged-dictionary mutation; defaults to a replace-in-place swap.</param>
    /// <param name="accentApplier">Applies/removes the accent brush; defaults to setting <c>PanelAccentBrush</c>.</param>
    internal ThemeManager(
        ILogger logger,
        ResourceDictionary applicationResources,
        Func<string, ResourceDictionary?>? themeLoader = null,
        Action<ResourceDictionary, ResourceDictionary, ResourceDictionary?>? swap = null,
        Action<ResourceDictionary, string?>? accentApplier = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _applicationResources = applicationResources ?? throw new ArgumentNullException(nameof(applicationResources));
        _themeLoader = themeLoader ?? LoadThemeDictionary;
        _swap = swap ?? SwapTheme;
        _accentApplier = accentApplier ?? ApplyAccentCore;

        AdoptActiveTheme();
    }

    /// <summary>
    /// Raised after a theme has been applied successfully. Carries the previous and new canonical
    /// theme ids so consumers can refresh theme-derived state without polling.
    /// </summary>
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>Gets the canonical id of the currently active theme.</summary>
    public string CurrentThemeId => _currentThemeId;

    /// <summary>
    /// Gets the current global accent color as normalized <c>#RRGGBB</c>/<c>#AARRGGBB</c>, or
    /// <see langword="null"/> when the theme accent is in effect (M3-RF-843).
    /// </summary>
    public string? CurrentAccentColor => _currentAccent;

    /// <summary>Gets the currently active theme dictionary, when one has been adopted or applied.</summary>
    internal ResourceDictionary? ActiveThemeDictionary => _activeThemeDictionary;

    /// <summary>
    /// Applies a built-in theme by id, keeping the current global accent. Unknown/missing ids fall
    /// back to dark with a warning; a loading or swapping failure leaves the previous theme fully in
    /// effect and returns <see langword="false"/>.
    /// </summary>
    /// <param name="themeId">The requested built-in theme id (case-insensitive, may be null).</param>
    /// <returns><see langword="true"/> when the requested (normalized) theme is active afterwards.</returns>
    public bool Apply(string? themeId) => Apply(themeId, _currentAccent);

    /// <summary>
    /// Applies a built-in theme and the global accent in one atomic, rollback-safe operation
    /// (M3-RF-843/M3-RF-848). Unknown/missing theme ids fall back to dark; invalid/blank accent
    /// values fall back to the theme accent; either failure restores the previous theme and accent
    /// wholesale and returns <see langword="false"/>.
    /// </summary>
    /// <param name="themeId">The requested built-in theme id (case-insensitive, may be null).</param>
    /// <param name="accentColor">The global accent as <c>#RRGGBB</c>/<c>#AARRGGBB</c>, or <see langword="null"/> for the theme accent.</param>
    /// <returns><see langword="true"/> when the requested theme/accent are active afterwards.</returns>
    public bool Apply(string? themeId, string? accentColor)
    {
        var known = ThemeIds.TryNormalize(themeId, out var requested);
        if (!known)
        {
            _logger.Warning(
                "Theme: '{RequestedTheme}' is not a built-in theme; falling back to '{FallbackTheme}'",
                string.IsNullOrWhiteSpace(themeId) ? "(missing)" : themeId,
                ThemeIds.Dark);
        }

        var normalizedAccent = NormalizeAccent(accentColor);
        var themeChanged = _activeThemeDictionary is null
            || !string.Equals(_currentThemeId, requested, StringComparison.Ordinal);
        var accentChanged = !string.Equals(_currentAccent, normalizedAccent, StringComparison.Ordinal);

        if (!themeChanged && !accentChanged)
        {
            return true;
        }

        ResourceDictionary? newTheme = null;
        if (themeChanged)
        {
            try
            {
                newTheme = _themeLoader(requested);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Theme: loading '{ThemeId}' failed; keeping '{CurrentThemeId}'", requested, _currentThemeId);
                return false;
            }

            if (newTheme is null)
            {
                _logger.Error(
                    "Theme: loading '{ThemeId}' produced no resource dictionary; keeping '{CurrentThemeId}'",
                    requested,
                    _currentThemeId);
                return false;
            }
        }

        var previousId = _currentThemeId;
        if (!TryApply(newTheme, themeChanged, requested, normalizedAccent))
        {
            // TryApply restored the previous merged-dictionary and accent state wholesale.
            return false;
        }

        if (themeChanged)
        {
            _activeThemeDictionary = newTheme;
            _currentThemeId = requested;
            RaiseThemeChanged(requested, previousId);
        }

        _currentAccent = normalizedAccent;
        return true;
    }

    private bool TryApply(ResourceDictionary? newTheme, bool themeChanged, string requested, string? accentColor)
    {
        var merged = _applicationResources.MergedDictionaries;

        // Snapshot the whole merged-dictionary list and the accent entry so a failed or partial
        // application can be undone wholesale (no mix of old and new palette keys, M3-RF-848).
        var snapshot = merged.ToArray();
        var hadAccent = _applicationResources.Contains(AccentBrushKey);
        var previousAccent = hadAccent ? _applicationResources[AccentBrushKey] : null;

        try
        {
            if (themeChanged)
            {
                _swap(_applicationResources, newTheme!, _activeThemeDictionary);
            }

            _accentApplier(_applicationResources, accentColor);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Theme: applying '{ThemeId}' failed; restoring '{PreviousThemeId}' wholesale",
                requested,
                _currentThemeId);

            Restore(merged, snapshot);
            RestoreAccent(hadAccent, previousAccent);
            return false;
        }
    }

    private string? NormalizeAccent(string? accentColor)
    {
        if (accentColor is null)
        {
            return null;
        }

        if (WidgetAppearance.TryNormalizeColor(accentColor, out var normalized))
        {
            return normalized;
        }

        _logger.Warning("Theme: the global accent color is invalid; using the theme accent");
        return null;
    }

    private static void ApplyAccentCore(ResourceDictionary resources, string? accentColor)
    {
        if (accentColor is null)
        {
            resources.Remove(AccentBrushKey);
            return;
        }

        var brush = CreateAccentBrush(accentColor);
        if (brush is null)
        {
            resources.Remove(AccentBrushKey);
            return;
        }

        resources[AccentBrushKey] = brush;
    }

    private static Brush? CreateAccentBrush(string normalizedColor)
    {
        var hex = normalizedColor.AsSpan(1);
        byte alpha;
        byte red;
        byte green;
        byte blue;
        if (hex.Length == 8)
        {
            alpha = ParseByte(hex[..2]);
            red = ParseByte(hex.Slice(2, 2));
            green = ParseByte(hex.Slice(4, 2));
            blue = ParseByte(hex.Slice(6, 2));
        }
        else if (hex.Length == 6)
        {
            alpha = 0xFF;
            red = ParseByte(hex[..2]);
            green = ParseByte(hex.Slice(2, 2));
            blue = ParseByte(hex.Slice(4, 2));
        }
        else
        {
            return null;
        }

        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static byte ParseByte(ReadOnlySpan<char> hex) =>
        byte.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private void RestoreAccent(bool hadAccent, object? previousAccent)
    {
        try
        {
            if (hadAccent && previousAccent is not null)
            {
                _applicationResources[AccentBrushKey] = previousAccent;
            }
            else
            {
                _applicationResources.Remove(AccentBrushKey);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Theme: restoring the previous accent failed; the application resources may be inconsistent");
        }
    }

    private void Restore(IList<ResourceDictionary> merged, ResourceDictionary[] snapshot)
    {
        try
        {
            merged.Clear();
            foreach (var dictionary in snapshot)
            {
                merged.Add(dictionary);
            }
        }
        catch (Exception ex)
        {
            // Last-resort containment: never let a rollback failure take the application down
            // (M3-RNF-138). Structured logging records the residual risk.
            _logger.Error(ex, "Theme: restoring the previous theme failed; the application resources may be inconsistent");
        }
    }

    private void AdoptActiveTheme()
    {
        foreach (var dictionary in _applicationResources.MergedDictionaries)
        {
            if (dictionary.Contains(ThemeIds.ThemeIdKey)
                && dictionary[ThemeIds.ThemeIdKey] is string id)
            {
                _activeThemeDictionary = dictionary;
                _currentThemeId = ThemeIds.Normalize(id);
                return;
            }
        }
    }

    private void RaiseThemeChanged(string themeId, string previousThemeId)
    {
        var handler = ThemeChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new ThemeChangedEventArgs(themeId, previousThemeId));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Theme: a ThemeChanged subscriber failed; the theme remains applied");
        }
    }

    private static void SwapTheme(ResourceDictionary host, ResourceDictionary newTheme, ResourceDictionary? previous)
    {
        var merged = host.MergedDictionaries;
        if (previous is not null)
        {
            var index = merged.IndexOf(previous);
            if (index >= 0)
            {
                merged[index] = newTheme;
                return;
            }
        }

        merged.Add(newTheme);
    }

    private static ResourceDictionary? LoadThemeDictionary(string themeId) =>
        ThemeIds.TryGetResourceUri(themeId, out var uri) && Application.LoadComponent(uri) is ResourceDictionary dictionary
            ? dictionary
            : null;

    private static ResourceDictionary GetApplicationResources() =>
        Application.Current?.Resources
        ?? throw new InvalidOperationException(
            "ThemeManager requires a WPF Application instance created on the UI thread.");
}
