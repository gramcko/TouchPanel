using TouchPanel.Config;

namespace TouchPanel.Actions.Handlers;

/// <summary>
/// Send-keys handler (RF-057, M2-RF-572): synthesizes one simultaneous key + modifier combination
/// into the <b>foreground</b> application through the <see cref="IKeyInput"/> seam (the only
/// <c>SendInput</c> implementation lives under <c>Interop/Input</c>). Modifiers are pressed down
/// before the target key and released after, and are <b>always released</b> in a <c>finally</c> so a
/// partial failure can never leave a modifier stuck (M2-RF-572/MR-3).
/// </summary>
/// <remarks>
/// <para><b>Off the UI thread (RF-050/RNF-004).</b> The synthesis sequence runs on the thread pool,
/// so a slow/blocked <c>SendInput</c> can never freeze the WPF dispatcher and the modifiers cannot
/// be left held while the UI thread stalls. The down/up events are issued in one synchronous block
/// with no await between them (M2-RF-572).</para>
/// <para><b>Focus semantics (M2-RF-573).</b> Keys are delivered to whatever window currently owns
/// the foreground; this handler never activates TouchPanel or changes the foreground. Combined with
/// a global hotkey (T-09) the combination therefore reaches the focused application.</para>
/// <para><b>Validation (M2-RF-575).</b> Unknown key/modifier and modifier-only combinations are
/// normally disabled at load; if one reaches the handler it fails fast without synthesizing input.
/// The M1 single-action <see cref="ActionExecutionResult"/> contract is unchanged (M2-RF-576).</para>
/// </remarks>
public sealed class SendKeysHandler : IActionHandler
{
    private readonly IKeyInput _input;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendKeysHandler"/> class.
    /// </summary>
    /// <param name="input">The key-synthesis boundary.</param>
    public SendKeysHandler(IKeyInput input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <inheritdoc />
    public string ActionType => ActionTypes.SendKeys;

    /// <inheritdoc />
    public Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not SendKeysActionConfig sendKeys)
        {
            throw new ArgumentException(
                $"The {nameof(SendKeysHandler)} requires a {nameof(SendKeysActionConfig)}; got {action.GetType().Name}.",
                nameof(action));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!SendKeyCombination.TryParse(sendKeys, out var combination, out var parseError))
        {
            // RF-047 fail-fast: an unknown/modifier-only combination was disabled at load and
            // executes nothing.
            return Task.FromResult(ActionExecutionResult.Failed(parseError ?? "Invalid send-keys combination."));
        }

        // RF-050/RNF-004: run the whole modifier-down → target key → modifier-up sequence on the
        // thread pool so input synthesis can never block (or be interrupted on) the WPF UI thread.
        // The sequence stays a single synchronous block, so no modifier is ever held across an
        // await/dispatcher hop and the modifiers are released promptly even on failure.
        return Task.Run(() => Execute(combination!), cancellationToken);
    }

    /// <summary>
    /// Presses the modifiers, presses and releases the target key, then releases the modifiers. The
    /// modifier releases run even when the target-key events fail (M2-RF-572).
    /// </summary>
    private ActionExecutionResult Execute(SendKeyCombination combination)
    {
        var pressedModifiers = new List<ushort>(combination.ModifierVirtualKeys.Count);
        var targetKeyDown = false;

        try
        {
            foreach (var modifier in combination.ModifierVirtualKeys)
            {
                if (!_input.TryKeyDown(modifier, out var modifierError))
                {
                    return ActionExecutionResult.Failed(
                        modifierError ?? $"Modifier key press failed for '{combination.KeyName}'.");
                }

                pressedModifiers.Add(modifier);
            }

            if (!_input.TryKeyDown(combination.VirtualKey, out var keyDownError))
            {
                return ActionExecutionResult.Failed(
                    keyDownError ?? $"Key press failed for '{combination.KeyName}'.");
            }

            targetKeyDown = true;

            if (!_input.TryKeyUp(combination.VirtualKey, out var keyUpError))
            {
                return ActionExecutionResult.Failed(
                    keyUpError ?? $"Key release failed for '{combination.KeyName}'.");
            }

            targetKeyDown = false;
            return ActionExecutionResult.Succeeded();
        }
        finally
        {
            // Never leave the target key or a modifier held (M2-RF-572).
            if (targetKeyDown)
            {
                _input.TryKeyUp(combination.VirtualKey, out _);
            }

            for (var index = pressedModifiers.Count - 1; index >= 0; index--)
            {
                _input.TryKeyUp(pressedModifiers[index], out _);
            }
        }
    }
}
