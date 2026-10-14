using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Widgets;

/// <summary>
/// Maps a config widget <c>type</c> to its view model factory and template key and builds the view
/// models for a page (DD-6/RF-032/RF-033). It implements the <see cref="IWidgetViewModelFactory"/>
/// boundary consumed by <see cref="PageViewModel"/>, so the host and the page model contain no
/// per-widget-instance logic and no type switch: adding a type is one registration plus one
/// template (RF-046 readiness).
/// </summary>
/// <remarks>
/// An unregistered type never drops the widget: <see cref="Create"/> returns an
/// <see cref="UnsupportedWidgetViewModel"/> (disabled, error indicator) per RF-047/RF-079. The
/// default catalog registered by <see cref="CreateDefault"/> is Button, Toggle, Label, Gauge
/// (RF-033/M1) plus Slider (RF-040/M2), Sparkline (RF-041/M2), Clock, ImageButton, and Bar
/// (RF-046/M3), plus the user-requested Image type.
/// </remarks>
public sealed class WidgetRegistry : IWidgetViewModelFactory, IWidgetPaletteSource
{
    private readonly Dictionary<string, WidgetRegistration> _registrations = new(StringComparer.Ordinal);

    // Insertion-ordered catalog backing the palette so the offered types are deterministic
    // (M2-RF-431); the dictionary above keeps O(1) type lookup.
    private readonly List<WidgetRegistration> _catalog = [];
    private readonly WidgetCreationContext _context;

    /// <summary>
    /// Initializes a new empty registry. Registrations are added with <see cref="Register"/>.
    /// </summary>
    /// <param name="context">The shared widget dependencies; defaults to an empty context.</param>
    public WidgetRegistry(WidgetCreationContext? context = null)
    {
        _context = context ?? new WidgetCreationContext();
    }

    /// <summary>Gets the registered widget types with their template keys and factories.</summary>
    public IReadOnlyCollection<WidgetRegistration> Registrations => _registrations.Values;

    /// <summary>
    /// Creates the registry with the widget catalog: Button, Toggle, Label, and Gauge (RF-033/M1)
    /// plus Slider (RF-040/M2), Sparkline (RF-041/M2), Clock, ImageButton, and Bar (RF-046/M3).
    /// </summary>
    /// <param name="context">The shared widget dependencies.</param>
    /// <returns>A registry with the default widget registrations.</returns>
    public static WidgetRegistry CreateDefault(WidgetCreationContext? context = null)
    {
        var creationContext = context ?? new WidgetCreationContext();

        return new WidgetRegistry(creationContext)
            .Register(new WidgetRegistration(
                WidgetTypes.Button,
                WidgetTemplateKeys.Button,
                (config, ctx) => new ButtonWidgetViewModel(
                    (ButtonWidgetConfig)config,
                    ctx.ActionExecutor,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger),
                displayName: "Button",
                createDefaultConfig: () => new ButtonWidgetConfig
                {
                    // RF-048/RF-574: a media key is the safe, recommended default.
                    Action = new MuteActionConfig(),
                },
                defaultWidth: 160,
                defaultHeight: 120))
            .Register(new WidgetRegistration(
                WidgetTypes.Toggle,
                WidgetTemplateKeys.Toggle,
                (config, ctx) => new ToggleWidgetViewModel(
                    (ToggleWidgetConfig)config,
                    ctx.ActionExecutor,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger,
                    ctx.PageId,
                    ctx.ToggleStateLookup,
                    ctx.ToggleStateChanged),
                displayName: "Toggle",
                createDefaultConfig: () => new ToggleWidgetConfig
                {
                    InitialOn = false,
                    Action = new MuteActionConfig(),
                },
                defaultWidth: 160,
                defaultHeight: 120))
            .Register(new WidgetRegistration(
                WidgetTypes.Label,
                WidgetTemplateKeys.Label,
                (config, ctx) => new LabelWidgetViewModel(
                    (LabelWidgetConfig)config,
                    ctx.TimeProvider,
                    ctx.Logger),
                displayName: "Label",
                createDefaultConfig: () => new LabelWidgetConfig { Label = "Label" },
                defaultWidth: 160,
                defaultHeight: 120))
            .Register(new WidgetRegistration(
                WidgetTypes.Gauge,
                WidgetTemplateKeys.Gauge,
                (config, ctx) => new GaugeWidgetViewModel(
                    (GaugeWidgetConfig)config,
                    ctx.SnapshotSource,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger),
                displayName: "Gauge",
                createDefaultConfig: () => new GaugeWidgetConfig
                {
                    Sensor = SensorKeys.CpuTotal,
                    Minimum = 0,
                    Maximum = 100,
                    Label = "CPU",
                },
                defaultWidth: 240,
                defaultHeight: 180))
            .Register(new WidgetRegistration(
                WidgetTypes.Slider,
                WidgetTemplateKeys.Slider,
                (config, ctx) => new SliderWidgetViewModel(
                    (SliderWidgetConfig)config,
                    ctx.ActionExecutor,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger),
                displayName: "Slider",
                createDefaultConfig: () => new SliderWidgetConfig
                {
                    Minimum = 0,
                    Maximum = 100,
                    Step = 1,
                    Value = 50,
                    Orientation = SliderOrientation.Horizontal,
                    Label = "Volume",
                    // RF-040/M2-RF-403: volume Set is the only M2 numeric binding.
                    Action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = 50 },
                },
                defaultWidth: 240,
                defaultHeight: 120))
            .Register(new WidgetRegistration(
                WidgetTypes.Sparkline,
                WidgetTemplateKeys.Sparkline,
                (config, ctx) => new SparklineWidgetViewModel(
                    (SparklineWidgetConfig)config,
                    ctx.History,
                    ctx.SnapshotSource,
                    ctx.RequestHistoryTracking,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger),
                displayName: "Sparkline",
                createDefaultConfig: () => new SparklineWidgetConfig
                {
                    Sensor = SensorKeys.CpuTotal,
                    WindowSeconds = SparklineWidgetConfig.DefaultWindowSeconds,
                    Style = SparklineStyle.Line,
                    Label = "CPU",
                },
                defaultWidth: 240,
                defaultHeight: 120))
            .Register(new WidgetRegistration(
                WidgetTypes.Clock,
                WidgetTemplateKeys.Clock,
                (config, ctx) => new ClockWidgetViewModel(
                    (ClockWidgetConfig)config,
                    ctx.ClockStateSource,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger,
                    clockCulture: ctx.ClockCulture),
                displayName: "Clock",
                createDefaultConfig: () => new ClockWidgetConfig
                {
                    Mode = ClockMode.Time24,
                },
                defaultWidth: 240,
                defaultHeight: 120))
            .Register(new WidgetRegistration(
                WidgetTypes.ImageButton,
                WidgetTemplateKeys.ImageButton,
                (config, ctx) => new ImageButtonWidgetViewModel(
                    (ImageButtonWidgetConfig)config,
                    ctx.ActionExecutor,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger),
                displayName: "Image Button",
                createDefaultConfig: () => new ImageButtonWidgetConfig
                {
                    Icon = "power",
                    ShowLabel = true,
                    Action = new MuteActionConfig(),
                },
                defaultWidth: 120,
                defaultHeight: 120))
            .Register(new WidgetRegistration(
                WidgetTypes.Bar,
                WidgetTemplateKeys.Bar,
                (config, ctx) => new BarWidgetViewModel(
                    (BarWidgetConfig)config,
                    ctx.SnapshotSource,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger),
                displayName: "Bar",
                createDefaultConfig: () => new BarWidgetConfig
                {
                    Sensor = SensorKeys.CpuTotal,
                    Minimum = BarWidgetConfig.DefaultMinimum,
                    Maximum = BarWidgetConfig.DefaultMaximum,
                    ShowValue = true,
                    Label = "CPU",
                },
                defaultWidth: 240,
                defaultHeight: 64))
            .Register(new WidgetRegistration(
                WidgetTypes.Image,
                WidgetTemplateKeys.Image,
                (config, ctx) => new ImageWidgetViewModel(
                    (ImageWidgetConfig)config,
                    ctx.TimeProvider,
                    ctx.Dispatch,
                    ctx.Logger),
                displayName: "Image",
                createDefaultConfig: () => new ImageWidgetConfig
                {
                    // The path is intentionally left unset: the user picks a local image. Until then
                    // the widget shows its placeholder and is disabled with a visible reason.
                    Stretch = ImageStretchMode.Uniform,
                    Label = "Image",
                },
                defaultWidth: 240,
                defaultHeight: 160));
    }

    /// <summary>
    /// Registers one widget type. Registering the same <c>type</c> twice is a composition error and
    /// throws, so the mapping stays deterministic.
    /// </summary>
    /// <param name="registration">The registration to add.</param>
    /// <returns>This registry, so default registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registration"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the type is already registered.</exception>
    public WidgetRegistry Register(WidgetRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!_registrations.TryAdd(registration.Type, registration))
        {
            throw new ArgumentException(
                $"A widget registration for type '{registration.Type}' already exists.",
                nameof(registration));
        }

        _catalog.Add(registration);
        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetPaletteItem> BuiltInWidgets =>
        _catalog
            .Where(registration => registration.CreateDefaultConfig is not null)
            .Select(registration => new WidgetPaletteItem(
                registration.Type,
                registration.DisplayName,
                registration.DefaultWidth,
                registration.DefaultHeight))
            .ToArray();

    /// <inheritdoc />
    public bool TryCreateDefaultWidget(string type, out WidgetConfig? config)
    {
        config = null;
        if (string.IsNullOrWhiteSpace(type)
            || !_registrations.TryGetValue(type, out var registration)
            || registration.CreateDefaultConfig is not { } createConfig)
        {
            return false;
        }

        config = createConfig();
        return true;
    }

    /// <summary>Looks up the registration for a widget type.</summary>
    /// <param name="type">The config <c>type</c> discriminator.</param>
    /// <param name="registration">The registration when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the type is registered.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is empty.</exception>
    public bool TryGet(string type, out WidgetRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return _registrations.TryGetValue(type, out registration!);
    }

    /// <summary>
    /// Gets the template key for a widget type, or <see cref="WidgetTemplateKeys.Unsupported"/> when
    /// the type is not registered.
    /// </summary>
    /// <param name="type">The config <c>type</c> discriminator.</param>
    /// <returns>The template resource key to resolve.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is empty.</exception>
    public string GetTemplateKey(string type) =>
        TryGet(type, out var registration) ? registration.TemplateKey : WidgetTemplateKeys.Unsupported;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public WidgetViewModelBase Create(WidgetConfig config) => Create(config, pageId: null);

    /// <summary>
    /// Creates the view model for one validated widget within a known page. The page id is placed on
    /// a copy of the shared context so page-scoped behavior (persisted Toggle state, RF-045) can be
    /// wired without every widget factory taking an extra parameter.
    /// </summary>
    /// <param name="config">The validated widget config; must not be <see langword="null"/>.</param>
    /// <param name="pageId">The owning page id, or <see langword="null"/> when unknown.</param>
    /// <returns>The widget view model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public WidgetViewModelBase Create(WidgetConfig config, string? pageId)
    {
        ArgumentNullException.ThrowIfNull(config);

        var context = pageId is null ? _context : _context with { PageId = pageId };

        if (_registrations.TryGetValue(config.Type, out var registration))
        {
            var viewModel = registration.Factory(config, context);

            // M3-RF-844/M3-RF-845: every widget resolves its icon through the shared active icon
            // pack so a live pack change refreshes realized glyphs without a rebuild.
            viewModel.AttachAppearanceSource(context.AppearanceSource);
            return viewModel;
        }

        // RF-047/RF-079: keep the widget visible but disabled with an error indicator. The
        // validation reason is preferred when present because it carries the config context.
        var reason = string.IsNullOrWhiteSpace(config.DisabledReason)
            ? $"Widget type '{config.Type}' is not supported by this build."
            : config.DisabledReason;

        var unsupported = new UnsupportedWidgetViewModel(
            config,
            reason,
            context.TimeProvider,
            context.Dispatch,
            context.Logger);
        unsupported.AttachAppearanceSource(context.AppearanceSource);
        return unsupported;
    }
}
