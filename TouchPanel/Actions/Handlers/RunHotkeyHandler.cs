using TouchPanel.Config;

namespace TouchPanel.Actions.Handlers;

/// <summary>
/// Hotkey-reference handler (M5-RF-521): resolves the referenced hotkey's bound action through
/// <see cref="IHotkeyActionResolver"/> and executes <b>that</b> action through the existing
/// <see cref="IActionExecutor"/> boundary. It introduces no new execution surface: the resolved
/// action is the same already-validated action the hotkey itself would run, so no shell string and
/// no elevation is added (M5-RNF-556).
/// </summary>
/// <remarks>
/// <para><b>No chaining (M5-RF-521/F-M5-06).</b> The injected <see cref="IActionExecutor"/> is the
/// core executor that does not itself register a hotkey handler, and the resolved action is
/// additionally rejected when it is another hotkey reference, so a reference can never recurse.</para>
/// <para><b>Off the UI thread (RF-050/RNF-004).</b> The resolved action is dispatched on the thread
/// pool, so a widget tap that references a hotkey never blocks the WPF dispatcher and the nested
/// dispatch cannot re-enter it while a resolved send-keys action holds modifiers.</para>
/// <para><b>Failure contract.</b> A missing/blank reference or a target with no usable action returns
/// a failed result instead of throwing; <see cref="ActionExecutor"/> still contains anything that
/// escapes (RF-050).</para>
/// </remarks>
public sealed class RunHotkeyHandler : IActionHandler
{
    private readonly IHotkeyActionResolver _resolver;
    private readonly IActionExecutor _executor;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunHotkeyHandler"/> class.
    /// </summary>
    /// <param name="resolver">Resolves the referenced hotkey's action from the live configuration; must not be <see langword="null"/>.</param>
    /// <param name="executor">
    /// The executor the resolved action is dispatched through. Production passes the core executor
    /// (without a hotkey handler) so chaining is structurally impossible; must not be
    /// <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is <see langword="null"/>.</exception>
    public RunHotkeyHandler(IHotkeyActionResolver resolver, IActionExecutor executor)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <inheritdoc />
    public string ActionType => ActionTypes.Hotkey;

    /// <inheritdoc />
    public async Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not RunHotkeyActionConfig hotkey)
        {
            throw new ArgumentException(
                $"The {nameof(RunHotkeyHandler)} requires a {nameof(RunHotkeyActionConfig)}; got {action.GetType().Name}.",
                nameof(action));
        }

        if (!_resolver.TryResolveAction(hotkey.HotkeyId, out var target) || target is null)
        {
            return ActionExecutionResult.Failed("The referenced hotkey is not configured.");
        }

        // M5-RF-521: a hotkey reference must not run another hotkey reference (no chaining).
        if (target is RunHotkeyActionConfig)
        {
            return ActionExecutionResult.Failed("A hotkey reference cannot run another hotkey reference.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // RF-050/RNF-004: invoke the core executor on the thread pool so the nested dispatch never
        // runs (or re-enters) on the WPF UI thread. Combined with each handler running off the UI
        // thread, a resolved send-keys action can never hold modifiers across a dispatcher hop and
        // the nested path cannot deadlock the message pump.
        return await Task.Run(
            () => _executor.ExecuteAsync(target, widgetId: null, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }
}
