using TouchPanel.Actions;
using TouchPanel.Config;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Configurable <see cref="IActionExecutor"/> double for widget tests: records every invocation
/// (action, widget id, token), can return a programmable result, and can throw to prove the widget
/// boundary contains executor violations (RF-050).
/// </summary>
internal sealed class RecordingActionExecutor : IActionExecutor
{
    /// <summary>Gets or sets the result factory; defaults to an immediate success.</summary>
    public Func<ActionConfig, string?, ActionExecutionResult>? ResultFactory { get; set; }

    /// <summary>Gets or sets an exception thrown instead of returning a result.</summary>
    public Exception? ThrowOnExecute { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked on the calling thread for every execution, before the result
    /// is produced. Used to prove dispatch happens off the UI thread.
    /// </summary>
    public Action<ActionConfig>? OnInvoke { get; set; }

    /// <summary>Gets the invocations received, in order.</summary>
    public List<RecordedInvocation> Invocations { get; } = [];

    /// <summary>Gets the number of invocations.</summary>
    public int InvocationCount => Invocations.Count;

    /// <inheritdoc />
    public Task<ActionExecutionResult> ExecuteAsync(
        ActionConfig action,
        string? widgetId = null,
        CancellationToken cancellationToken = default)
    {
        OnInvoke?.Invoke(action);
        Invocations.Add(new RecordedInvocation(action, widgetId, cancellationToken));

        if (ThrowOnExecute is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(ResultFactory?.Invoke(action, widgetId) ?? ActionExecutionResult.Succeeded());
    }

    /// <summary>One recorded executor call.</summary>
    internal sealed record RecordedInvocation(ActionConfig Action, string? WidgetId, CancellationToken CancellationToken);
}
