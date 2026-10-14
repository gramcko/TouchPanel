using Serilog;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// Label widget view model (RF-033/RF-034): display-only. It never executes an action (the base
/// <see cref="WidgetViewModelBase.ActivateCoreAsync"/> is a no-op) and never shows the failure
/// indicator; the template renders the configured <see cref="WidgetViewModelBase.Label"/> text.
/// </summary>
public sealed class LabelWidgetViewModel : WidgetViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LabelWidgetViewModel"/> class.
    /// </summary>
    /// <param name="config">The validated label config.</param>
    /// <param name="timeProvider">Clock for the error window (unused by labels, kept for symmetry).</param>
    /// <param name="logger">Logger for widget-level failures.</param>
    public LabelWidgetViewModel(
        LabelWidgetConfig config,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.Label, actionExecutor: null, timeProvider, dispatch: null, logger)
    {
    }
}
