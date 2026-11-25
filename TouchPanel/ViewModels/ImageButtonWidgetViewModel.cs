using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// ImageButton widget view model (RF-046/M3-RF-464/465): a touch-first button that renders the
/// configured built-in icon (or the label / a neutral default when no icon is set) and executes
/// exactly one bound action per completed tap. It reuses the base action machinery, so a
/// missing/invalid action leaves the widget visible but disabled with an error indicator (RF-047)
/// and edit mode suppresses execution (M2-RF-439).
/// </summary>
/// <remarks>
/// The widget is non-activating: the template's touch style is <c>Focusable="False"</c>, so a tap
/// never takes keyboard focus from another application (RF-018/RF-025). No external image files are
/// read; only the built-in icon catalog is used (RF-081).
/// </remarks>
public sealed class ImageButtonWidgetViewModel : WidgetViewModelBase
{
    /// <summary>
    /// Neutral Segoe MDL2 glyph shown when neither an icon nor a visible label is configured, so the
    /// button never renders empty (M3-RF-464).
    /// </summary>
    public const string DefaultGlyph = "\uE700";

    private bool _showLabel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageButtonWidgetViewModel"/> class.
    /// </summary>
    /// <param name="config">The validated image-button config.</param>
    /// <param name="actionExecutor">The action boundary (RF-050).</param>
    /// <param name="timeProvider">Clock for the transient error window; defaults to the system clock.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for widget-level failures.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public ImageButtonWidgetViewModel(
        ImageButtonWidgetConfig config,
        IActionExecutor? actionExecutor = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.ImageButton, actionExecutor, timeProvider, dispatch, logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        _showLabel = config.ShowLabel;
    }

    /// <summary>Gets a value indicating whether the label is shown beneath the icon.</summary>
    public bool ShowLabel => _showLabel;

    /// <summary>Gets the glyph to render: the configured icon glyph, or the neutral default when no icon is set.</summary>
    public string DisplayGlyph => IconGlyph ?? (IconPathData is null ? DefaultGlyph : string.Empty);

    /// <summary>
    /// Gets the vector path to render for a configured brand or line-art icon, or
    /// <see langword="null"/> for a glyph/default icon (user-requested brand and line-art icons).
    /// </summary>
    public string? DisplayPathData => IconPathData;

    /// <summary>
    /// Gets a value indicating whether <see cref="DisplayPathData"/> is line art drawn with a stroked
    /// outline rather than a filled shape.
    /// </summary>
    public bool DisplayStroke => IconStroke;

    /// <summary>Gets the stroke pen width in the 24-unit viewbox for a line-art icon, or <c>0</c>.</summary>
    public double DisplayStrokeUnits => IconStrokeUnits;

    /// <summary>Gets a value indicating whether the icon (or default glyph) is visible.</summary>
    public bool ShowIcon => HasIcon || !(ShowLabel && HasLabel);

    /// <summary>Gets a value indicating whether a visible label is configured and enabled.</summary>
    public bool ShowLabelText => ShowLabel && HasLabel;

    /// <summary>Gets a value indicating whether a non-empty label is configured.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <inheritdoc />
    public override void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        base.ApplyConfig(config);

        if (config is not ImageButtonWidgetConfig imageButton)
        {
            return;
        }

        if (_showLabel != imageButton.ShowLabel)
        {
            _showLabel = imageButton.ShowLabel;
            OnPropertyChanged(nameof(ShowLabel));
        }

        OnPropertyChanged(nameof(DisplayGlyph));
        OnPropertyChanged(nameof(DisplayPathData));
        OnPropertyChanged(nameof(DisplayStroke));
        OnPropertyChanged(nameof(DisplayStrokeUnits));
        OnPropertyChanged(nameof(ShowIcon));
        OnPropertyChanged(nameof(ShowLabelText));
        OnPropertyChanged(nameof(HasLabel));
    }

    /// <inheritdoc />
    protected override void OnIconGlyphChanged()
    {
        // M3-RF-844: a live icon-pack swap re-resolves the glyph through the active pack; the
        // projected display properties must follow without a rebuild.
        OnPropertyChanged(nameof(DisplayGlyph));
        OnPropertyChanged(nameof(DisplayPathData));
        OnPropertyChanged(nameof(DisplayStroke));
        OnPropertyChanged(nameof(DisplayStrokeUnits));
        OnPropertyChanged(nameof(ShowIcon));
    }

    /// <inheritdoc />
    protected override Task ActivateCoreAsync(CancellationToken cancellationToken) =>
        ExecuteBoundActionAsync(cancellationToken);
}
