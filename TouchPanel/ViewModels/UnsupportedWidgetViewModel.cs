using Serilog;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// Placeholder view model for a widget whose type has no registration in this build (RF-047/
/// RF-079): the widget is kept in the page and rendered disabled with an error indicator instead
/// of being dropped, so the user can see and fix the configuration. Unknown action bindings do not
/// reach this type — those keep their Button/Toggle template and are disabled by validation.
/// </summary>
public sealed class UnsupportedWidgetViewModel : WidgetViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedWidgetViewModel"/> class.
    /// </summary>
    /// <param name="config">The validated widget config (possibly with raw, unnormalized geometry).</param>
    /// <param name="reason">Why the type is unsupported; a generated message is used when omitted.</param>
    /// <param name="timeProvider">Clock for the error window (unused; kept for symmetry).</param>
    /// <param name="dispatch">UI-thread marshalling delegate; unused because the placeholder is static.</param>
    /// <param name="logger">Logger for widget-level failures.</param>
    public UnsupportedWidgetViewModel(
        WidgetConfig config,
        string? reason = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.Unsupported, actionExecutor: null, timeProvider, dispatch, logger)
    {
        UnsupportedReason = string.IsNullOrWhiteSpace(reason)
            ? $"Widget type '{WidgetType}' is not supported by this build."
            : reason;

        // The placeholder is always non-interactive regardless of what validation concluded for
        // the underlying config (for example an enabled Gauge before T-11 registers it).
        IsEnabled = false;
        DisabledReason = UnsupportedReason;
    }

    /// <summary>Gets the human-readable reason the widget cannot be rendered (eventually shown by the template).</summary>
    public string UnsupportedReason { get; }

    /// <summary>
    /// Gets a value indicating that edit mode must not manipulate this placeholder: its raw JSON is
    /// preserved verbatim and cannot persist geometry changes (RF-079).
    /// </summary>
    public override bool CanEditGeometry => false;

    /// <summary>Gets the caption to display: the configured label, or the raw type when unlabeled.</summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? $"[{WidgetType}]" : Label!;
}
