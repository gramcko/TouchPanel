using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// Button widget view model (RF-033/RF-035): one completed tap (WPF fires on release inside the
/// control) executes the bound action exactly once through the shared executor helper. Renders
/// with the Button template and carries no per-instance logic (DD-6).
/// </summary>
public sealed class ButtonWidgetViewModel : WidgetViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ButtonWidgetViewModel"/> class.
    /// </summary>
    /// <param name="config">The validated button config.</param>
    /// <param name="actionExecutor">The action boundary (RF-050).</param>
    /// <param name="timeProvider">Clock for the transient error window; defaults to the system clock.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for widget-level failures.</param>
    public ButtonWidgetViewModel(
        ButtonWidgetConfig config,
        IActionExecutor? actionExecutor = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.Button, actionExecutor, timeProvider, dispatch, logger)
    {
    }

    /// <inheritdoc />
    protected override Task ActivateCoreAsync(CancellationToken cancellationToken) =>
        ExecuteBoundActionAsync(cancellationToken);
}
