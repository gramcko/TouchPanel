using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Logging;

namespace TouchPanel.Actions;

/// <summary>
/// The M1 action dispatch boundary (DD-8, RF-047..RF-053). Resolves an <see cref="IActionHandler"/>
/// by the config <c>type</c> discriminator through a small dictionary and guarantees the failure
/// contract: no exception escapes, failures are logged with the action type and widget id, and the
/// returned message is redaction-safe (RF-050, RF-051, RF-085).
/// </summary>
/// <remarks>
/// <para><b>Unknown types fail fast.</b> T-04 validation disables widgets with unsupported action
/// types at load; if one reaches the executor anyway (for example a programmatic call), it fails
/// immediately with a result instead of executing anything (RF-047, RF-081).</para>
/// <para><b>Redaction.</b> Every parameter-bearing log message and returned failure message passes
/// through <see cref="LogRedactor"/>; parameters themselves are only ever logged in redacted form
/// (RF-051/RF-085).</para>
/// <para><b>WPF-free.</b> This namespace contains no WPF types so actions stay testable without a
/// UI (RF-053).</para>
/// </remarks>
public sealed class ActionExecutor : IActionExecutor
{
    private readonly Dictionary<string, IActionHandler> _handlers;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionExecutor"/> class.
    /// </summary>
    /// <param name="handlers">The handlers to dispatch to; each declares exactly one action type.</param>
    /// <param name="logger">Logger for action start/failure entries; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentException">Two handlers declare the same <see cref="IActionHandler.ActionType"/>.</exception>
    public ActionExecutor(IEnumerable<IActionHandler> handlers, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        _handlers = new Dictionary<string, IActionHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(handler.ActionType);

            if (!_handlers.TryAdd(handler.ActionType, handler))
            {
                throw new ArgumentException(
                    $"Duplicate action handler registered for type '{handler.ActionType}'.",
                    nameof(handlers));
            }
        }

        _logger = (logger ?? Log.Logger).ForContext<ActionExecutor>();
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> ExecuteAsync(
        ActionConfig action,
        string? widgetId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_handlers.TryGetValue(action.Type, out var handler))
        {
            // RF-047/RF-081: an unsupported type executes nothing and fails fast.
            return Fail(action, widgetId, $"Unsupported action type '{action.Type}'.", exception: null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.CanceledResult();
        }

        _logger.Debug(
            "Action {ActionType} starting for widget {WidgetId} ({Parameters})",
            action.Type,
            widgetId ?? "(none)",
            DescribeParameters(action));

        try
        {
            var result = await handler.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                return Fail(action, widgetId, "The action handler returned no result.", exception: null);
            }

            if (result.Success)
            {
                return result;
            }

            if (result.Canceled)
            {
                _logger.Information(
                    "Action {ActionType} for widget {WidgetId} was canceled",
                    action.Type,
                    widgetId ?? "(none)");
                return result;
            }

            return Fail(action, widgetId, result.Message, exception: null);
        }
        catch (OperationCanceledException)
        {
            _logger.Information(
                "Action {ActionType} for widget {WidgetId} was canceled",
                action.Type,
                widgetId ?? "(none)");
            return ActionExecutionResult.CanceledResult();
        }
        catch (Exception exception)
        {
            // RF-050: missing executable, access denied, input-synthesis failure, handler bugs, ...
            // all stay inside this boundary.
            return Fail(action, widgetId, exception.Message, exception);
        }
    }

    private ActionExecutionResult Fail(
        ActionConfig action,
        string? widgetId,
        string message,
        Exception? exception)
    {
        var safeMessage = LogRedactor.RedactText(message);
        if (string.IsNullOrWhiteSpace(safeMessage))
        {
            safeMessage = "The action failed.";
        }

        _logger.Warning(
            exception,
            "Action {ActionType} failed for widget {WidgetId} ({Parameters}): {Reason}",
            action.Type,
            widgetId ?? "(none)",
            DescribeParameters(action),
            safeMessage);

        return ActionExecutionResult.Failed(safeMessage);
    }

    /// <summary>
    /// Builds a redaction-safe, structured description of the action parameters for logs. Raw
    /// parameter values are never logged (RF-051/RF-085).
    /// </summary>
    private static string DescribeParameters(ActionConfig action) => action switch
    {
        RunCommandActionConfig run =>
            $"executable='{LogRedactor.RedactText(run.Path)}', arguments=[{string.Join(", ", run.Arguments.Select(LogRedactor.RedactText))}]",
        MediaKeyActionConfig media => $"key={media.Key}",
        VolumeActionConfig volume => volume.Mode == VolumeMode.Set
            ? $"mode=set, level={volume.Level}"
            : $"mode=step, direction={volume.Direction}",
        MuteActionConfig => "mode=toggle",
        SendKeysActionConfig send =>
            $"key='{LogRedactor.RedactText(send.Key)}', modifiers=[{string.Join(", ", send.Modifiers.Select(LogRedactor.RedactText))}]",
        RunHotkeyActionConfig hotkey => $"hotkeyId='{LogRedactor.RedactText(hotkey.HotkeyId)}'",
        UnknownActionConfig unknown => $"type='{LogRedactor.RedactText(unknown.Type)}'",
        _ => $"type='{LogRedactor.RedactText(action.Type)}'",
    };
}
