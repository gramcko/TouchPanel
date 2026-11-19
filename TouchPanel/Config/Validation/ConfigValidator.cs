using System.Globalization;
using System.IO;
using TouchPanel.Monitoring;
using TouchPanel.Services.Time;
using TouchPanel.Theme;

namespace TouchPanel.Config.Validation;

/// <summary>
/// Pure, WPF-free, two-tier validation of a deserialized <see cref="PanelConfig"/> (RF-079,
/// DD-9, D-13).
/// </summary>
/// <remarks>
/// <para><b>Contract.</b> The validator assumes the store already performed file-level checks
/// (the text parsed as JSON, the root is an object, it carries a supported <c>schemaVersion</c>,
/// and typed deserialization succeeded). File-level problems route to the recovery path in
/// <c>JsonConfigStore</c> and never reach this class. The validator only contains per-entry and
/// page/root-level problems, and it never throws for hostile data.</para>
/// <para><b>Normalization.</b> Validation normalizes the passed configuration in place so the
/// caller can run with it directly: geometry is clamped into the page bounds, widgets with
/// invalid bindings are marked disabled (kept, never dropped), missing ids are synthesized,
/// unknown enum sentinels are replaced by safe defaults, and an empty page list gains the
/// generated default page. Every normalization is reported through a diagnostic with a stable
/// code.</para>
/// <para><b>Rate scale.</b> The one silent normalization: a Bar/Gauge bound to a bytes-per-second
/// sensor whose range is still the untouched 0–100 default adopts 0–12,500,000 B/s (100 Mbps),
/// because rates are published in bytes per second and a 0–100 scale would pin the meter at 100%.
/// It is not a finding the user can act on, so it emits no diagnostic; an explicit range is never
/// touched (see <see cref="ShouldUseRateDefaultRange"/>).</para>
/// <para><b>Page bounds.</b> Schema v1 has no page-size field, so load-time clamping uses
/// <see cref="MaxWidgetExtent"/> as the maximum supported panel extent. The runtime host still
/// clamps against the actual monitor bounds (T-08/T-12).</para>
/// </remarks>
public static class ConfigValidator
{
    /// <summary>Minimum width/height of an interactive widget (D-9/RF-031), in DIPs.</summary>
    public const double MinWidgetSize = 40;

    /// <summary>Maximum supported panel extent used as the load-time page bound, in DIPs.</summary>
    public const double MaxWidgetExtent = 16384;

    /// <summary>Minimum useful span of a gauge display range.</summary>
    public const double MinGaugeSpan = 1;

    /// <summary>
    /// The untouched default lower bound of a bar/gauge display range (identical to
    /// <see cref="BarWidgetConfig.DefaultMinimum"/> and <see cref="GaugeWidgetConfig.DefaultMinimum"/>).
    /// </summary>
    public const double DefaultRangeMinimum = 0;

    /// <summary>
    /// The untouched default upper bound of a bar/gauge display range (identical to
    /// <see cref="BarWidgetConfig.DefaultMaximum"/> and <see cref="GaugeWidgetConfig.DefaultMaximum"/>).
    /// </summary>
    public const double DefaultRangeMaximum = 100;

    /// <summary>
    /// Lower bound of the unit-aware default range adopted for a bytes-per-second bar/gauge.
    /// </summary>
    public const double DefaultRateMinimum = 0;

    /// <summary>
    /// Upper bound of the unit-aware default range adopted for a bytes-per-second bar/gauge:
    /// 12,500,000 bytes/s (100 Mbps). Rates are published in bytes per second (millions for any real
    /// network traffic), so the untouched 0–100 default would pin the meter at 100% forever.
    /// </summary>
    public const double DefaultRateMaximum = 12_500_000;

    /// <summary>Sane upper magnitude for gauge range endpoints.</summary>
    public const double MaxGaugeMagnitude = 1e9;

    /// <summary>Lowest value accepted for an absolute volume level.</summary>
    public const int MinVolumeLevel = 0;

    /// <summary>Highest value accepted for an absolute volume level.</summary>
    public const int MaxVolumeLevel = 100;

    /// <summary>Maximum accepted label length in characters (RF-043/M2-RF-435), mirroring the edit UI.</summary>
    public const int MaxLabelLength = 128;

    /// <summary>Maximum accepted page-name length in characters, mirroring the pages editor.</summary>
    public const int MaxPageNameLength = 64;

    /// <summary>
    /// Validates and normalizes <paramref name="config"/> in place (see the class remarks for the
    /// exact contract) and returns the diagnostics produced. The result is never
    /// <see langword="null"/>; an empty list means nothing required normalization.
    /// </summary>
    /// <param name="config">The deserialized configuration to validate and normalize.</param>
    /// <param name="clockCulture">
    /// The canonical Clock display-culture source (M4-RF-951) used to validate a custom Clock
    /// format. The composition root supplies the same instance to the Clock view model so validation
    /// and display agree; when <see langword="null"/>, <see cref="ClockCultureSource.Default"/> is
    /// used (the process's current culture captured once).
    /// </param>
    /// <returns>The diagnostics, in the order the findings were detected.</returns>
    public static IReadOnlyList<ConfigDiagnostic> Validate(PanelConfig config, IClockCultureSource? clockCulture = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var diagnostics = new List<ConfigDiagnostic>();
        var culture = (clockCulture ?? ClockCultureSource.Default).Culture;

        ValidateTelemetry(config, diagnostics);
        ValidateMonitor(config, diagnostics);
        ValidateWindow(config, diagnostics);
        ValidateAppearance(config, diagnostics);
        EnsurePages(config, diagnostics);
        ValidatePages(config, config.Hotkeys, culture, diagnostics);
        ResolveActivePage(config, diagnostics);

        return diagnostics;
    }

    /// <summary>
    /// Validates and normalizes one widget's bound action in isolation using the exact rules
    /// <see cref="Validate"/> applies (RF-047, M2-RF-434), without touching pages, geometry, or
    /// telemetry. The action is normalized in place (null argument/modifier entries removed, a
    /// volume level clamped); on an invalid or missing action the widget is disabled with a reason.
    /// A caller that is re-enabling a widget after an edit must set
    /// <see cref="WidgetConfig.IsEnabled"/> to <see langword="true"/> and clear
    /// <see cref="WidgetConfig.DisabledReason"/> before calling, so a now-valid action is not left
    /// disabled; an action that is still invalid re-disables the widget.
    /// </summary>
    /// <param name="widget">The widget whose action is validated; must not be <see langword="null"/>.</param>
    /// <param name="hotkeys">
    /// The effective configured hotkeys, used to cross-check a <c>hotkey</c> reference (M5-RF-523);
    /// when <see langword="null"/>, a hotkey reference is validated as if no hotkey existed.
    /// </param>
    /// <returns>The diagnostics produced, in the order the findings were detected.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<ConfigDiagnostic> ValidateWidgetAction(
        WidgetConfig widget,
        IReadOnlyList<HotkeyConfig>? hotkeys = null)
    {
        ArgumentNullException.ThrowIfNull(widget);

        var diagnostics = new List<ConfigDiagnostic>();
        ValidateAction(widget, hotkeys, diagnostics);
        return diagnostics;
    }

    private static void ValidateTelemetry(PanelConfig config, List<ConfigDiagnostic> diagnostics)
    {
        var interval = config.Telemetry.PollIntervalSeconds;
        var normalized = double.IsFinite(interval) ? interval : TelemetryConfig.DefaultPollIntervalSeconds;
        normalized = Math.Clamp(
            normalized,
            TelemetryConfig.MinPollIntervalSeconds,
            TelemetryConfig.MaxPollIntervalSeconds);

        if (!normalized.Equals(interval))
        {
            config.Telemetry.PollIntervalSeconds = normalized;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.telemetry.interval",
                $"The telemetry poll interval was clamped to {Format(normalized)} s "
                + $"({Format(TelemetryConfig.MinPollIntervalSeconds)}–{Format(TelemetryConfig.MaxPollIntervalSeconds)} s)."));
        }
    }

    private static void ValidateMonitor(PanelConfig config, List<ConfigDiagnostic> diagnostics)
    {
        if (config.Monitor.Fallback == MonitorFallback.Unknown)
        {
            config.Monitor.Fallback = MonitorFallback.None;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.monitor.fallback",
                "Unknown monitor fallback value; using 'none' (stay hidden when the target monitor is unavailable)."));
        }

        var identity = config.Monitor.Match;
        if (identity is null)
        {
            return;
        }

        if (identity.HardwareId is not null && string.IsNullOrWhiteSpace(identity.HardwareId))
        {
            identity.HardwareId = null;
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.monitor.identity",
                "A blank monitor hardware id was ignored."));
        }

        if (identity.DevicePath is not null && string.IsNullOrWhiteSpace(identity.DevicePath))
        {
            identity.DevicePath = null;
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.monitor.identity",
                "A blank monitor device path was ignored."));
        }

        if (identity.FriendlyName is not null && string.IsNullOrWhiteSpace(identity.FriendlyName))
        {
            identity.FriendlyName = null;
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.monitor.identity",
                "A blank monitor friendly name was ignored."));
        }
    }

    /// <summary>
    /// Normalizes the optional <c>window.pageTransition</c> value (user-requested addition, additive
    /// under schema v4): ids are case-insensitive and normalized to their canonical spelling without
    /// a warning; an unknown value falls back to <c>slide</c> with a warning, so a single bad entry
    /// never fails the whole file and older files (property missing) keep the documented default.
    /// </summary>
    /// <param name="config">The configuration whose window section is validated.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    private static void ValidateWindow(PanelConfig config, List<ConfigDiagnostic> diagnostics)
    {
        var window = config.Window;
        var configured = window.PageTransition;
        if (!PageTransitionModes.TryNormalize(configured, out var canonical))
        {
            window.PageTransition = canonical;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.window.pageTransition",
                $"Unknown page transition '{configured}'; using '{canonical}'."));
        }
        else if (!string.Equals(configured, canonical, StringComparison.Ordinal))
        {
            window.PageTransition = canonical;
        }
    }

    /// <summary>
    /// Normalizes the global <c>appearance</c> section (M3-RF-841/M3-RF-847): an unknown theme falls
    /// back to <c>dark</c>, an unknown icon pack to <c>default</c>, and an invalid/blank accent
    /// color to <see langword="null"/> ("use the theme accent") — each with a logged warning. A
    /// missing section already carries the documented defaults from the model
    /// (<see cref="AppearanceConfig"/>). Ids are case-insensitive and are normalized to their
    /// canonical spelling without a warning. None of these is a file-level error, so an otherwise
    /// valid file still loads and runs (RF-079).
    /// </summary>
    /// <param name="config">The configuration whose appearance is validated.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    private static void ValidateAppearance(PanelConfig config, List<ConfigDiagnostic> diagnostics)
    {
        var appearance = config.Appearance;

        var theme = appearance.Theme;
        if (!ThemeIds.TryNormalize(theme, out var canonicalTheme))
        {
            appearance.Theme = canonicalTheme;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.appearance.theme",
                $"Unknown appearance theme '{theme}'; using '{canonicalTheme}'."));
        }
        else if (!string.Equals(theme, canonicalTheme, StringComparison.Ordinal))
        {
            appearance.Theme = canonicalTheme;
        }

        var iconPack = appearance.IconPack;
        if (!IconPackIds.TryNormalize(iconPack, out var canonicalPack))
        {
            appearance.IconPack = canonicalPack;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.appearance.iconPack",
                $"Unknown icon pack '{iconPack}'; using '{canonicalPack}'."));
        }
        else if (!string.Equals(iconPack, canonicalPack, StringComparison.Ordinal))
        {
            appearance.IconPack = canonicalPack;
        }

        var accentColor = appearance.AccentColor;
        if (accentColor is null)
        {
            return;
        }

        if (WidgetAppearance.TryNormalizeColor(accentColor, out var normalizedAccent))
        {
            if (!string.Equals(accentColor, normalizedAccent, StringComparison.Ordinal))
            {
                appearance.AccentColor = normalizedAccent;
            }

            return;
        }

        appearance.AccentColor = null;
        diagnostics.Add(ConfigDiagnostic.Warning(
            "config.appearance.accentColor",
            "The global accent color was invalid; the theme accent is used."));
    }

    private static void EnsurePages(PanelConfig config, List<ConfigDiagnostic> diagnostics)
    {
        if (config.Pages.Count > 0)
        {
            return;
        }

        config.Pages.Add(DefaultConfigGenerator.CreateDefaultPage());
        diagnostics.Add(ConfigDiagnostic.Warning(
            "config.pages.empty",
            "The configuration contains no pages; the generated default page was added."));
    }

    private static void ValidatePages(
        PanelConfig config,
        IReadOnlyList<HotkeyConfig> hotkeys,
        CultureInfo clockCulture,
        List<ConfigDiagnostic> diagnostics)
    {
        var pageIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < config.Pages.Count; index++)
        {
            ValidatePage(config.Pages[index], index, pageIds, hotkeys, clockCulture, diagnostics);
        }
    }

    private static void ValidatePage(
        PageConfig page,
        int pageIndex,
        HashSet<string> pageIds,
        IReadOnlyList<HotkeyConfig> hotkeys,
        CultureInfo clockCulture,
        List<ConfigDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(page.Id))
        {
            page.Id = GenerateUniqueId($"page-{pageIndex + 1}", pageIds);
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.page.missingId",
                $"A page without an id was assigned the generated id '{page.Id}'."));
        }

        if (!pageIds.Add(page.Id))
        {
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.page.duplicateId",
                $"Duplicate page id '{page.Id}'; only the first page with this id can be activated."));
        }

        NormalizePageName(page, diagnostics);
        NormalizePageBackground(page, diagnostics);

        var widgetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var widget in page.Widgets)
        {
            if (!string.IsNullOrWhiteSpace(widget.Id) && !widgetIds.Add(widget.Id))
            {
                // Non-fatal (RF-079): duplicates stay visible and enabled, but geometry write-back
                // matches by id, so the user is warned that edits may land on the wrong widget.
                diagnostics.Add(ConfigDiagnostic.Warning(
                    "config.widget.duplicateId",
                    $"Duplicate widget id '{widget.Id}' on page '{page.Id}'; geometry changes are matched by id and may apply to the wrong widget."));
            }
        }

        for (var index = 0; index < page.Widgets.Count; index++)
        {
            ValidateWidget(page, page.Widgets[index], index, widgetIds, hotkeys, clockCulture, diagnostics);
        }
    }

    /// <summary>
    /// Normalizes the optional page label (additive under schema v4): blank names fall back to the id
    /// (stored as <see langword="null"/>), and an over-long name is truncated to
    /// <see cref="MaxPageNameLength"/> with a warning. Hostile input never throws.
    /// </summary>
    /// <param name="page">The page whose name is normalized.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    private static void NormalizePageName(PageConfig page, List<ConfigDiagnostic> diagnostics)
    {
        var name = page.Name;
        if (name is null)
        {
            return;
        }

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            page.Name = null;
            return;
        }

        if (trimmed.Length > MaxPageNameLength)
        {
            page.Name = trimmed[..MaxPageNameLength];
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.page.name",
                $"Page '{page.Id}' had a name longer than {MaxPageNameLength} characters; it was truncated."));
            return;
        }

        if (!string.Equals(trimmed, name, StringComparison.Ordinal))
        {
            page.Name = trimmed;
        }
    }

    /// <summary>
    /// Normalizes the optional page background (user-requested addition, additive under schema
    /// v4): a valid color is normalized to uppercase, an invalid one is cleared with a warning, and
    /// a background image must be an absolute path to an existing, recognizable raster file or it
    /// is cleared with a warning. The page itself always stays usable and the load never fails.
    /// </summary>
    /// <param name="page">The page whose background is normalized.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    private static void NormalizePageBackground(PageConfig page, List<ConfigDiagnostic> diagnostics)
    {
        var color = page.Background;
        if (color is not null)
        {
            if (WidgetAppearance.TryNormalizeColor(color, out var normalizedColor))
            {
                if (!string.Equals(color, normalizedColor, StringComparison.Ordinal))
                {
                    page.Background = normalizedColor;
                }
            }
            else
            {
                page.Background = null;
                diagnostics.Add(ConfigDiagnostic.Warning(
                    "config.page.background",
                    $"Page '{page.Id}' had an invalid background color; the theme background is used."));
            }
        }

        var image = page.BackgroundImage;
        if (image is null)
        {
            return;
        }

        if (PageBackground.TryNormalizeImagePath(image, out var normalizedImage))
        {
            if (!string.Equals(image, normalizedImage, StringComparison.Ordinal))
            {
                page.BackgroundImage = normalizedImage;
            }
        }
        else
        {
            page.BackgroundImage = null;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.page.backgroundImage",
                $"Page '{page.Id}' had a background image that is not an absolute path to a readable image; no image is used."));
        }
    }

    private static void ValidateWidget(
        PageConfig page,
        WidgetConfig widget,
        int widgetIndex,
        HashSet<string> widgetIds,
        IReadOnlyList<HotkeyConfig> hotkeys,
        CultureInfo clockCulture,
        List<ConfigDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(widget.Id))
        {
            widget.Id = GenerateUniqueId($"widget-{widgetIndex + 1}", widgetIds);
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.widget.missingId",
                $"A widget without an id on page '{page.Id}' was assigned the generated id '{widget.Id}'."));
        }

        // Unknown widget types are preserved and disabled with an error indicator (RF-047,
        // RF-079); their raw geometry is left untouched so no information is lost.
        if (widget is UnknownWidgetConfig)
        {
            Disable(
                widget,
                diagnostics,
                "config.widget.unknownType",
                $"Widget '{widget.Id}' has the unsupported type '{widget.Type}' and was disabled.");
            return;
        }

        ValidateGeometry(page, widget, diagnostics);
        ValidateAppearance(widget, diagnostics);

        switch (widget)
        {
            case GaugeWidgetConfig gauge:
                ValidateGauge(gauge, diagnostics);
                break;
            case SliderWidgetConfig slider:
                ValidateSlider(slider, diagnostics);
                break;
            case SparklineWidgetConfig sparkline:
                ValidateSparkline(sparkline, diagnostics);
                break;
            case ClockWidgetConfig clock:
                ValidateClock(clock, clockCulture, diagnostics);
                break;
            case BarWidgetConfig bar:
                ValidateBar(bar, diagnostics);
                break;
            case ImageWidgetConfig image:
                ValidateImage(image, diagnostics);
                break;
        }
        ValidateAction(widget, hotkeys, diagnostics);
    }

    /// <summary>
    /// Normalizes one widget's label and appearance overrides (RF-043/M2-RF-435/M2-RF-436): an
    /// over-long label is truncated to <see cref="MaxLabelLength"/>; an unknown icon id and invalid
    /// color values fall back to the theme default; a font size and opacity outside their bounds are
    /// clamped. Every change is reported as a diagnostic. Hostile configuration never throws.
    /// </summary>
    /// <param name="widget">The widget whose appearance is validated; must not be <see langword="null"/>.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    private static void ValidateAppearance(WidgetConfig widget, List<ConfigDiagnostic> diagnostics)
    {
        if (widget.Label is { Length: > MaxLabelLength } label)
        {
            widget.Label = label[..MaxLabelLength];
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.label",
                $"Widget '{widget.Id}' had a label longer than {MaxLabelLength} characters; it was truncated."));
        }

        if (widget.Icon is not null && !IconCatalog.TryGet(widget.Icon, out _))
        {
            widget.Icon = null;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.icon",
                $"Widget '{widget.Id}' referenced an icon that is not in the built-in catalog; no icon is shown."));
        }

        // User-requested addition: an optional icon that replaces the label text. It is additive, so
        // a missing/null value stays null (the text label renders) and only an unknown id is cleared.
        if (widget.LabelIcon is not null && !IconCatalog.TryGet(widget.LabelIcon, out _))
        {
            widget.LabelIcon = null;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.labelIcon",
                $"Widget '{widget.Id}' referenced a label icon that is not in the built-in catalog; no label icon is shown."));
        }

        NormalizeColor("foreground", widget.Foreground, value => widget.Foreground = value, widget, diagnostics);
        NormalizeColor("background", widget.Background, value => widget.Background = value, widget, diagnostics);
        NormalizeColor("accent", widget.Accent, value => widget.Accent = value, widget, diagnostics);

        var fontSize = WidgetAppearance.NormalizeFontSize(widget.FontSize);
        if (!Nullable.Equals(fontSize, widget.FontSize))
        {
            widget.FontSize = fontSize;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.appearance.fontSize",
                $"Widget '{widget.Id}' font size was clamped to {Format(fontSize ?? WidgetAppearance.MinFontSize)} DIP "
                + $"({Format(WidgetAppearance.MinFontSize)}–{Format(WidgetAppearance.MaxFontSize)})."));
        }

        var opacity = WidgetAppearance.NormalizeOpacity(widget.Opacity);
        if (!Nullable.Equals(opacity, widget.Opacity))
        {
            widget.Opacity = opacity;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.appearance.opacity",
                $"Widget '{widget.Id}' opacity was clamped to {Format(opacity ?? WidgetAppearance.MinOpacity)} "
                + $"({Format(WidgetAppearance.MinOpacity)}–{Format(WidgetAppearance.MaxOpacity)})."));
        }
    }

    private static void NormalizeColor(
        string name,
        string? value,
        Action<string?> set,
        WidgetConfig widget,
        List<ConfigDiagnostic> diagnostics)
    {
        if (value is null)
        {
            return;
        }

        if (WidgetAppearance.TryNormalizeColor(value, out var normalized))
        {
            if (!string.Equals(normalized, value, StringComparison.Ordinal))
            {
                set(normalized);
            }

            return;
        }

        set(null);
        diagnostics.Add(ConfigDiagnostic.Warning(
            $"config.widget.appearance.{name}",
            $"Widget '{widget.Id}' had an invalid {name} color; the theme default was used."));
    }

    private static void ValidateGeometry(PageConfig page, WidgetConfig widget, List<ConfigDiagnostic> diagnostics)
    {
        var changes = new List<string>(4);

        var width = NormalizeExtent(widget.Width, "width", changes);
        var height = NormalizeExtent(widget.Height, "height", changes);
        var x = NormalizePosition(widget.X, MaxWidgetExtent - width, "x", changes);
        var y = NormalizePosition(widget.Y, MaxWidgetExtent - height, "y", changes);

        widget.X = x;
        widget.Y = y;
        widget.Width = width;
        widget.Height = height;

        if (changes.Count > 0)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.geometry",
                $"Widget '{widget.Id}' on page '{page.Id}' was clamped into the page bounds ({string.Join(", ", changes)})."));
        }
    }

    private static double NormalizeExtent(double value, string name, List<string> changes)
    {
        var normalized = value;
        if (!double.IsFinite(normalized))
        {
            normalized = MinWidgetSize;
        }

        normalized = Math.Clamp(normalized, MinWidgetSize, MaxWidgetExtent);
        if (!normalized.Equals(value))
        {
            changes.Add($"{name}={Format(value)}→{Format(normalized)}");
        }

        return normalized;
    }

    private static double NormalizePosition(double value, double max, string name, List<string> changes)
    {
        var normalized = double.IsFinite(value) ? Math.Clamp(value, 0, Math.Max(0, max)) : 0;
        if (!normalized.Equals(value))
        {
            changes.Add($"{name}={Format(value)}→{Format(normalized)}");
        }

        return normalized;
    }

    private static void ValidateGauge(GaugeWidgetConfig gauge, List<ConfigDiagnostic> diagnostics)
    {
        NormalizeDisplayUnit(gauge.DisplayUnit, value => gauge.DisplayUnit = value, $"Gauge '{gauge.Id}'", diagnostics);

        if (string.IsNullOrWhiteSpace(gauge.Sensor))
        {
            Disable(
                gauge,
                diagnostics,
                "config.widget.gauge.sensor",
                $"Gauge '{gauge.Id}' has no sensor configured and was disabled.");
            return;
        }

        var minimum = gauge.Minimum;
        var maximum = gauge.Maximum;
        var valid = double.IsFinite(minimum)
            && double.IsFinite(maximum)
            && Math.Abs(minimum) <= MaxGaugeMagnitude
            && Math.Abs(maximum) <= MaxGaugeMagnitude
            && maximum - minimum >= MinGaugeSpan;

        if (!valid)
        {
            gauge.Minimum = GaugeWidgetConfig.DefaultMinimum;
            gauge.Maximum = GaugeWidgetConfig.DefaultMaximum;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.gauge.range",
                $"Gauge '{gauge.Id}' had an invalid display range and was reset to 0–100."));
        }

        // Rate sensors (net/disk) publish bytes per second, so the untouched default range would show
        // 100% for any real traffic; see ShouldUseRateDefaultRange. Silent on purpose and never
        // applied to an explicit non-default range.
        if (ShouldUseRateDefaultRange(gauge.Sensor, gauge.Minimum, gauge.Maximum))
        {
            gauge.Minimum = DefaultRateMinimum;
            gauge.Maximum = DefaultRateMaximum;
        }
    }

    private static void ValidateSlider(SliderWidgetConfig slider, List<ConfigDiagnostic> diagnostics)
    {
        var minimum = slider.Minimum;
        var maximum = slider.Maximum;
        var step = slider.Step;
        var rangeValid = double.IsFinite(minimum)
            && double.IsFinite(maximum)
            && Math.Abs(minimum) <= MaxGaugeMagnitude
            && Math.Abs(maximum) <= MaxGaugeMagnitude
            && maximum - minimum > 0;
        var stepValid = double.IsFinite(step) && step > 0 && rangeValid && step <= maximum - minimum;

        if (!rangeValid || !stepValid)
        {
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Step = 1;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.slider.range",
                $"Slider '{slider.Id}' had an invalid range/step and was reset to 0–100 step 1."));
        }

        if (slider.Orientation == SliderOrientation.Unknown)
        {
            slider.Orientation = SliderOrientation.Horizontal;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.slider.orientation",
                $"Slider '{slider.Id}' had an unrecognized orientation; using 'horizontal'."));
        }

        var normalized = NormalizeSliderValue(slider.Value, slider.Minimum, slider.Maximum, slider.Step);
        if (!normalized.Equals(slider.Value))
        {
            slider.Value = normalized;
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.widget.slider.value",
                $"Slider '{slider.Id}' initial value was clamped/snapped to {Format(normalized)}."));
        }
    }

    /// <summary>
    /// Clamps <paramref name="value"/> into <c>[minimum, maximum]</c> and rounds it to the nearest
    /// whole step from <paramref name="minimum"/>, then re-clamps so floating-point drift can never
    /// escape the range. This is the exact rule <see cref="ValidateSlider"/> applies to a slider's
    /// configured initial value; it is exposed so edit-time validation (M2-RF-433) stays identical.
    /// </summary>
    /// <param name="value">The requested value; a non-finite value degrades to <paramref name="minimum"/>.</param>
    /// <param name="minimum">The range lower bound (assumed finite and valid after validation).</param>
    /// <param name="maximum">The range upper bound (assumed finite and valid after validation).</param>
    /// <param name="step">The step increment (assumed finite and positive after validation).</param>
    /// <returns>The clamped, step-snapped value.</returns>
    public static double NormalizeSliderValue(double value, double minimum, double maximum, double step)
    {
        var finite = double.IsFinite(value) ? value : minimum;
        var clamped = Math.Clamp(finite, minimum, maximum);
        return Math.Clamp(SnapToStep(clamped, minimum, step), minimum, maximum);
    }

    private static double SnapToStep(double value, double minimum, double step)
    {
        var steps = Math.Round((value - minimum) / step, MidpointRounding.AwayFromZero);
        return minimum + (steps * step);
    }

    private static void ValidateSparkline(SparklineWidgetConfig sparkline, List<ConfigDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(sparkline.Sensor))
        {
            Disable(
                sparkline,
                diagnostics,
                "config.widget.sparkline.sensor",
                $"Sparkline '{sparkline.Id}' has no sensor configured and was disabled.");
            return;
        }

        var window = sparkline.WindowSeconds;
        var normalized = double.IsFinite(window) ? window : SparklineWidgetConfig.DefaultWindowSeconds;
        normalized = Math.Clamp(
            normalized,
            SparklineWidgetConfig.MinWindowSeconds,
            SparklineWidgetConfig.MaxWindowSeconds);
        if (!normalized.Equals(window))
        {
            sparkline.WindowSeconds = normalized;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.sparkline.window",
                $"Sparkline '{sparkline.Id}' window was clamped to {Format(normalized)} s."));
        }

        if (sparkline.Style == SparklineStyle.Unknown)
        {
            sparkline.Style = SparklineStyle.Line;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.sparkline.style",
                $"Sparkline '{sparkline.Id}' had an unrecognized render style; using 'line'."));
        }

        NormalizeDisplayUnit(
            sparkline.DisplayUnit,
            value => sparkline.DisplayUnit = value,
            $"Sparkline '{sparkline.Id}'",
            diagnostics);

        ValidateSparklineScale(sparkline, diagnostics);
    }

    private static void ValidateSparklineScale(SparklineWidgetConfig sparkline, List<ConfigDiagnostic> diagnostics)
    {
        if (sparkline.Minimum is null && sparkline.Maximum is null)
        {
            return;
        }

        var finiteMinimum = sparkline.Minimum is double min && double.IsFinite(min) && Math.Abs(min) <= MaxGaugeMagnitude
            ? sparkline.Minimum
            : null;
        var finiteMaximum = sparkline.Maximum is double max && double.IsFinite(max) && Math.Abs(max) <= MaxGaugeMagnitude
            ? sparkline.Maximum
            : null;

        var rangeUsable = finiteMinimum is double lo
            && finiteMaximum is double hi
            && hi - lo >= MinGaugeSpan;

        if (rangeUsable)
        {
            sparkline.Minimum = finiteMinimum;
            sparkline.Maximum = finiteMaximum;
            return;
        }

        sparkline.Minimum = null;
        sparkline.Maximum = null;
        diagnostics.Add(ConfigDiagnostic.Warning(
            "config.widget.sparkline.scale",
            $"Sparkline '{sparkline.Id}' had an invalid explicit scale and was set to auto-scale."));
    }

    private static void ValidateClock(
        ClockWidgetConfig clock,
        CultureInfo clockCulture,
        List<ConfigDiagnostic> diagnostics)
    {
        if (clock.Mode == ClockMode.Unknown)
        {
            clock.Mode = ClockFormat.DefaultMode;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.clock.mode",
                $"Clock '{clock.Id}' had an unrecognized mode; using '{clock.Mode}'."));
        }

        if (clock.Format is null)
        {
            return;
        }

        // M3-RF-462/M3-RF-469 + M4-RF-951: an over-length or invalid custom format is ignored (set
        // to null) with a warning so the widget falls back to the mode preset; the check uses the
        // same canonical display culture as the Clock view model so validation and runtime agree.
        if (!ClockFormat.TryCustomFormat(clock.Format, clockCulture))
        {
            clock.Format = null;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.clock.format",
                $"Clock '{clock.Id}' had an invalid or over-length custom format; the mode preset is used."));
        }
    }

    private static void ValidateBar(BarWidgetConfig bar, List<ConfigDiagnostic> diagnostics)
    {
        NormalizeDisplayUnit(bar.DisplayUnit, value => bar.DisplayUnit = value, $"Bar '{bar.Id}'", diagnostics);

        if (string.IsNullOrWhiteSpace(bar.Sensor))
        {
            Disable(
                bar,
                diagnostics,
                "config.widget.bar.sensor",
                $"Bar '{bar.Id}' has no sensor configured and was disabled.");
            return;
        }

        // M3-RF-467: an unknown sensor key disables the widget with a reason rather than showing a
        // fabricated value; the entry is preserved (never dropped).
        if (!IsKnownSensorKey(bar.Sensor))
        {
            Disable(
                bar,
                diagnostics,
                "config.widget.bar.sensor",
                $"Bar '{bar.Id}' references the unknown sensor '{bar.Sensor}' and was disabled.");
            return;
        }

        var minimum = bar.Minimum;
        var maximum = bar.Maximum;
        var valid = double.IsFinite(minimum)
            && double.IsFinite(maximum)
            && Math.Abs(minimum) <= MaxGaugeMagnitude
            && Math.Abs(maximum) <= MaxGaugeMagnitude
            && maximum > minimum;

        if (!valid)
        {
            bar.Minimum = BarWidgetConfig.DefaultMinimum;
            bar.Maximum = BarWidgetConfig.DefaultMaximum;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.bar.range",
                $"Bar '{bar.Id}' had an invalid display range and was reset to "
                + $"{Format(BarWidgetConfig.DefaultMinimum)}–{Format(BarWidgetConfig.DefaultMaximum)}."));
        }

        // User-reported fix: net.down/net.up (and disk read/write) publish bytes per second — millions
        // for any real traffic — so a bar still carrying the untouched 0–100 default pinned itself at
        // 100%. Adopt the unit-aware 0–100 Mbps default instead. This is a silent normalization (no
        // diagnostic: nothing for the user to act on, the persisted file simply agrees) and an
        // explicit non-default range is never touched.
        if (ShouldUseRateDefaultRange(bar.Sensor, bar.Minimum, bar.Maximum))
        {
            bar.Minimum = DefaultRateMinimum;
            bar.Maximum = DefaultRateMaximum;
        }
    }

    /// <summary>
    /// Resolves whether a Bar/Gauge bound to <paramref name="sensorKey"/> should adopt the unit-aware
    /// default range instead of the untouched 0–100 default. True only when the configured range is
    /// still exactly <see cref="DefaultRangeMinimum"/>–<see cref="DefaultRangeMaximum"/> <em>and</em>
    /// the sensor resolves to <see cref="SensorUnit.BytesPerSecond"/>. Unknown/missing sensors, every
    /// other unit (percent, °C, RPM, MHz, V, W, bytes), and any explicit range return
    /// <see langword="false"/> so the caller never rewrites a deliberate scale.
    /// </summary>
    /// <param name="sensorKey">The configured sensor key; may be <see langword="null"/>.</param>
    /// <param name="minimum">The configured range lower bound.</param>
    /// <param name="maximum">The configured range upper bound.</param>
    /// <returns><see langword="true"/> when the range should become 0–12,500,000 bytes/s.</returns>
    public static bool ShouldUseRateDefaultRange(string? sensorKey, double minimum, double maximum) =>
        minimum.Equals(DefaultRangeMinimum)
        && maximum.Equals(DefaultRangeMaximum)
        && IsRateSensor(sensorKey);

    /// <summary>
    /// Gets a value indicating whether <paramref name="sensorKey"/> resolves to a sensor that
    /// publishes <see cref="SensorUnit.BytesPerSecond"/> (network and disk throughput). Unknown and
    /// missing keys are not rate sensors.
    /// </summary>
    /// <param name="sensorKey">The configured sensor key; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a known bytes-per-second sensor key.</returns>
    public static bool IsRateSensor(string? sensorKey) =>
        TryResolveUnit(sensorKey, out var unit) && unit == SensorUnit.BytesPerSecond;

    /// <summary>
    /// Resolves the unit a sensor key publishes without hardware access (the unit is fixed by the
    /// key, only the value is unavailable). Missing and unknown keys resolve nothing.
    /// </summary>
    private static bool TryResolveUnit(string? sensorKey, out SensorUnit unit)
    {
        if (!string.IsNullOrWhiteSpace(sensorKey)
            && SensorSnapshot.CreateUnavailable(DateTimeOffset.UnixEpoch).TryGetReading(sensorKey.Trim(), out var reading))
        {
            unit = reading.Unit;
            return true;
        }

        unit = SensorUnit.Percent;
        return false;
    }

    /// <summary>
    /// Normalizes an Image widget (user-requested addition): an unknown fit mode falls back to
    /// <c>uniform</c>, and the untrusted path must be non-empty and absolute — a relative path is
    /// rejected so it can never select an unintended file, and the entry is disabled (never
    /// dropped) with a visible reason. Validation never opens the file; missing/undecodable files
    /// are the view model's runtime placeholder concern.
    /// </summary>
    /// <param name="image">The Image widget configuration to validate.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    private static void ValidateImage(ImageWidgetConfig image, List<ConfigDiagnostic> diagnostics)
    {
        if (image.Stretch == ImageStretchMode.Unknown)
        {
            image.Stretch = ImageStretchMode.Uniform;
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.image.stretch",
                $"Image '{image.Id}' had an unrecognized fit mode; using 'uniform'."));
        }

        var path = image.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            Disable(
                image,
                diagnostics,
                "config.widget.image.path",
                $"Image '{image.Id}' has no image path configured and was disabled.");
            return;
        }

        var trimmed = path.Trim();
        if (!IsAbsolutePath(trimmed))
        {
            Disable(
                image,
                diagnostics,
                "config.widget.image.path",
                $"Image '{image.Id}' has a relative image path and was disabled; an absolute path is required.");
            return;
        }

        if (!string.Equals(trimmed, path, StringComparison.Ordinal))
        {
            image.Path = trimmed;
        }
    }

    private static bool IsAbsolutePath(string path)
    {
        try
        {
            return Path.IsPathRooted(path);
        }
        catch (Exception)
        {
            // Hostile input must never throw from validation; treat it as not absolute.
            return false;
        }
    }

    private static bool IsKnownSensorKey(string sensorKey) =>
        SensorSnapshot.CreateUnavailable(DateTimeOffset.UnixEpoch).TryGetReading(sensorKey, out _);

    /// <summary>
    /// Normalizes a byte/rate display unit (user-requested addition): the unrecognized sentinel falls
    /// back to <see cref="DisplayUnit.Auto"/> with a warning. The field is additive and optional, so a
    /// file that omits it already carries the <see cref="DisplayUnit.Auto"/> default and is
    /// unchanged; entry-level containment keeps one bad value from failing the whole file.
    /// </summary>
    /// <param name="unit">The configured unit.</param>
    /// <param name="set">Applies the normalized unit.</param>
    /// <param name="owner">A human-readable widget selector used in the diagnostic.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    private static void NormalizeDisplayUnit(
        DisplayUnit unit,
        Action<DisplayUnit> set,
        string owner,
        List<ConfigDiagnostic> diagnostics)
    {
        if (unit != DisplayUnit.Unknown)
        {
            return;
        }

        set(DisplayUnit.Auto);
        diagnostics.Add(ConfigDiagnostic.Warning(
            "config.widget.displayUnit",
            $"{owner} had an unrecognized display unit; using 'auto'."));
    }

    private static void ValidateAction(
        WidgetConfig widget,
        IReadOnlyList<HotkeyConfig>? hotkeys,
        List<ConfigDiagnostic> diagnostics)
    {
        var action = widget.Action;
        if (action is null)
        {
            if (widget is ButtonWidgetConfig or ToggleWidgetConfig or ImageButtonWidgetConfig)
            {
                Disable(
                    widget,
                    diagnostics,
                    "config.widget.action.missing",
                    $"Widget '{widget.Id}' has no action bound and was disabled.");
            }

            return;
        }

        switch (action)
        {
            case UnknownActionConfig:
                Disable(
                    widget,
                    diagnostics,
                    "config.widget.action.unknown",
                    $"Widget '{widget.Id}' binds the unsupported action type '{action.Type}' and was disabled.");
                break;

            case RunCommandActionConfig run:
                ValidateRunCommand(widget, run, diagnostics);
                break;

            case MediaKeyActionConfig media:
                if (media.Key == MediaKeyKind.Unknown)
                {
                    Disable(
                        widget,
                        diagnostics,
                        "config.widget.action.invalid",
                        $"Widget '{widget.Id}' binds an unrecognized media key and was disabled.");
                }

                break;

            case VolumeActionConfig volume:
                ValidateVolumeAction(widget, volume, diagnostics);
                break;

            case MuteActionConfig:
                break;

            case SendKeysActionConfig sendKeys:
                ValidateSendKeys(widget, sendKeys, diagnostics);
                break;

            case RunHotkeyActionConfig hotkey:
                ValidateRunHotkey(widget, hotkey, hotkeys, diagnostics);
                break;

            default:
                Disable(
                    widget,
                    diagnostics,
                    "config.widget.action.unknown",
                    $"Widget '{widget.Id}' binds the unsupported action type '{action.Type}' and was disabled.");
                break;
        }
    }

    private static void ValidateRunCommand(
        WidgetConfig widget,
        RunCommandActionConfig run,
        List<ConfigDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(run.Path))
        {
            Disable(
                widget,
                diagnostics,
                "config.widget.action.invalid",
                $"Widget '{widget.Id}' binds a run-command action without an executable path and was disabled.");
            return;
        }

        if (run.Arguments.RemoveAll(argument => argument is null) > 0)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.action.arguments",
                $"Widget '{widget.Id}' had null run-command argument entries; they were removed."));
        }

        if (run.WorkingDirectory is not null && string.IsNullOrWhiteSpace(run.WorkingDirectory))
        {
            run.WorkingDirectory = null;
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.widget.action.workingDirectory",
                $"Widget '{widget.Id}' had a blank working directory; it was ignored."));
        }
    }

    private static void ValidateVolumeAction(
        WidgetConfig widget,
        VolumeActionConfig volume,
        List<ConfigDiagnostic> diagnostics)
    {
        if (volume.Mode == VolumeMode.Unknown)
        {
            Disable(
                widget,
                diagnostics,
                "config.widget.action.invalid",
                $"Widget '{widget.Id}' binds a volume action with an unrecognized mode and was disabled.");
            return;
        }

        if (volume.Mode == VolumeMode.Set)
        {
            var clamped = Math.Clamp(volume.Level, MinVolumeLevel, MaxVolumeLevel);
            if (clamped != volume.Level)
            {
                volume.Level = clamped;
                diagnostics.Add(ConfigDiagnostic.Warning(
                    "config.widget.action.volume",
                    $"Widget '{widget.Id}' volume level was clamped to {clamped}."));
            }

            return;
        }

        if (volume.Direction == VolumeDirection.Unknown)
        {
            Disable(
                widget,
                diagnostics,
                "config.widget.action.invalid",
                $"Widget '{widget.Id}' binds a volume step action with an unrecognized direction and was disabled.");
        }
    }

    private static void ValidateSendKeys(
        WidgetConfig widget,
        SendKeysActionConfig sendKeys,
        List<ConfigDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(sendKeys.Key))
        {
            Disable(
                widget,
                diagnostics,
                "config.widget.action.sendKeys.key",
                $"Widget '{widget.Id}' binds a send-keys action without a key and was disabled.");
            return;
        }

        if (sendKeys.Modifiers.RemoveAll(modifier => modifier is null) > 0)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.widget.action.sendKeys.modifiers",
                $"Widget '{widget.Id}' had null send-keys modifier entries; they were removed."));
        }

        // RF-057/M2-RF-575: the friendly names are validated against the fixed mapping; an unknown
        // key or modifier disables the widget (RF-047). A bare key remains valid.
        if (!Actions.SendKeyCombination.TryMapKey(sendKeys.Key, out _))
        {
            Disable(
                widget,
                diagnostics,
                "config.widget.action.sendKeys.key",
                $"Widget '{widget.Id}' binds a send-keys action with the unknown key '{sendKeys.Key}' and was disabled.");
            return;
        }

        foreach (var modifier in sendKeys.Modifiers)
        {
            if (!Actions.SendKeyCombination.IsKnownModifier(modifier))
            {
                Disable(
                    widget,
                    diagnostics,
                    "config.widget.action.sendKeys.modifier",
                    $"Widget '{widget.Id}' binds a send-keys action with the unknown modifier '{modifier}' and was disabled.");
                return;
            }
        }
    }

    /// <summary>
    /// Cross-checks a <c>hotkey</c> action against the effective <c>config.Hotkeys</c> (M5-RF-523):
    /// a blank reference or an id that does not exist disables the widget with a visible reason and
    /// is never dropped. Duplicate ids resolve first-wins through
    /// <see cref="Actions.HotkeyActionResolver.TryResolve"/>; the target hotkey's own action validity
    /// is handled when the hotkey is applied, not here.
    /// </summary>
    /// <param name="widget">The widget carrying the reference.</param>
    /// <param name="hotkey">The hotkey-reference action.</param>
    /// <param name="hotkeys">The effective configured hotkeys.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    private static void ValidateRunHotkey(
        WidgetConfig widget,
        RunHotkeyActionConfig hotkey,
        IReadOnlyList<HotkeyConfig>? hotkeys,
        List<ConfigDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(hotkey.HotkeyId))
        {
            Disable(
                widget,
                diagnostics,
                "config.widget.action.hotkey.reference",
                $"Widget '{widget.Id}' binds a hotkey action without a hotkey id and was disabled.");
            return;
        }

        if (!Actions.HotkeyActionResolver.TryResolve(hotkeys, hotkey.HotkeyId, out _))
        {
            Disable(
                widget,
                diagnostics,
                "config.widget.action.hotkey.reference",
                $"Widget '{widget.Id}' references the hotkey '{hotkey.HotkeyId.Trim()}', which is not configured, and was disabled.");
        }
    }

    private static void ResolveActivePage(PanelConfig config, List<ConfigDiagnostic> diagnostics)
    {
        var requested = config.ActivePageId;
        var resolved = string.IsNullOrWhiteSpace(requested)
            ? null
            : config.Pages.Find(page => string.Equals(page.Id, requested, StringComparison.Ordinal));

        if (resolved is not null)
        {
            return;
        }

        var fallback = config.Pages[0];
        config.ActivePageId = fallback.Id;
        diagnostics.Add(ConfigDiagnostic.Warning(
            "config.activePage.substituted",
            requested is null
                ? $"No activePageId was configured; using the first page '{fallback.Id}'."
                : $"activePageId '{requested}' does not match any page; using the first page '{fallback.Id}'."));
    }

    private static void Disable(
        WidgetConfig widget,
        List<ConfigDiagnostic> diagnostics,
        string code,
        string message)
    {
        widget.IsEnabled = false;
        widget.DisabledReason = message;
        diagnostics.Add(ConfigDiagnostic.Warning(code, message));
    }

    private static string GenerateUniqueId(string baseId, HashSet<string> seen)
    {
        var candidate = baseId;
        var suffix = 1;
        while (!seen.Add(candidate))
        {
            candidate = $"{baseId}-{suffix++}";
        }

        return candidate;
    }

    private static string Format(double value) =>
        value.ToString(double.IsFinite(value) ? "R" : "G", CultureInfo.InvariantCulture);
}
