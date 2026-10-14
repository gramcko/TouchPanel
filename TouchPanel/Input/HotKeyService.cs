using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Notifications;

namespace TouchPanel.Input;

/// <summary>
/// Owns global hotkeys end to end (RF-058/RF-059, MD-3): it parses the configured <c>hotkeys</c>
/// list, registers each valid combination through <see cref="IHotKeyRegistration"/>, maps pressed
/// ids back to their binding, and dispatches the single bound action asynchronously through
/// <see cref="IActionExecutor"/>. It is WPF-free and depends on the registration seam, so it is
/// fully unit-testable without touching the operating system (M2-RNF-124/M2-RNF-125).
/// </summary>
/// <remarks>
/// <para><b>UI-thread affinity (M2-RF-582/MD-16).</b> <c>RegisterHotKey</c>/<c>UnregisterHotKey</c>
/// and the hidden window are thread-affine. Every public entry point therefore runs its work through
/// the injected <c>marshal</c> delegate; the composition root supplies <c>Dispatcher.Invoke</c> so
/// registration, re-registration, and disposal always happen on the UI thread.</para>
/// <para><b>Failure handling (RF-059).</b> An invalid entry is logged and skipped; a duplicate
/// combination (first wins, configuration order) is logged and skipped; an OS refusal is logged with
/// the full combination and the rest continue. After attempting all registrations one aggregated
/// notification lists the refused combinations. There is <b>no</b> retry loop.</para>
/// <para><b>Dispatch (M2-RF-583/M2-RF-585).</b> A pressed hotkey is fire-and-forget: it never blocks
/// the message pump, never activates the panel, and never changes the foreground.</para>
/// </remarks>
public sealed class HotKeyService : IDisposable
{
    private readonly IHotKeyRegistration _registration;
    private readonly IActionExecutor _executor;
    private readonly IUserNotifier _notifier;
    private readonly ILogger _logger;
    private readonly Action<Action> _marshal;
    private readonly Dictionary<int, HotKeyBinding> _active = new();
    private bool _subscribed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotKeyService"/> class.
    /// </summary>
    /// <param name="registration">The hotkey-registration boundary; the production implementation owns the hidden UI-thread window.</param>
    /// <param name="executor">The action boundary the bound action is dispatched through.</param>
    /// <param name="notifier">The RF-010 notification boundary used for the one aggregated failure notice.</param>
    /// <param name="logger">The application logger.</param>
    /// <param name="marshal">Executes an action on the UI thread (for example <c>Dispatcher.Invoke</c>).</param>
    public HotKeyService(
        IHotKeyRegistration registration,
        IActionExecutor executor,
        IUserNotifier notifier,
        ILogger logger,
        Action<Action> marshal)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        ArgumentNullException.ThrowIfNull(logger);
        _marshal = marshal ?? throw new ArgumentNullException(nameof(marshal));

        _logger = logger.ForContext<HotKeyService>();
    }

    /// <summary>
    /// Unregisters every previously registered hotkey and applies <paramref name="hotkeys"/> from the
    /// effective configuration (re-registration, M2-RF-595). Marshalled to the UI thread.
    /// </summary>
    /// <param name="hotkeys">The configured hotkeys in configuration order.</param>
    public void Apply(IReadOnlyList<HotkeyConfig> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        if (_disposed)
        {
            return;
        }

        _marshal(() => ApplyCore(hotkeys));
    }

    /// <summary>
    /// Unregisters every currently registered hotkey without registering new ones. Marshalled to the
    /// UI thread.
    /// </summary>
    public void UnregisterAll()
    {
        if (_disposed)
        {
            return;
        }

        _marshal(UnregisterAllCore);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _marshal(DisposeCore);
    }

    private void ApplyCore(IReadOnlyList<HotkeyConfig> hotkeys)
    {
        EnsureSubscribed();
        UnregisterAllCore();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var refused = new List<string>();
        var invalidCount = 0;
        var duplicateCount = 0;
        var registrationId = 1;

        foreach (var config in hotkeys)
        {
            if (!HotKeyParser.TryParse(config, registrationId, out var binding, out var error))
            {
                invalidCount++;
                _logger.Warning(
                    "Hotkeys: skipping invalid entry {HotkeyId}: {Reason}",
                    config?.Id,
                    error);
                registrationId++;
                continue;
            }

            // Duplicates are detected before registration so the first valid entry always wins and
            // later ones are skipped deterministically (M2-RF-593).
            if (!seen.Add(binding!.Identity))
            {
                duplicateCount++;
                _logger.Warning(
                    "Hotkeys: skipping duplicate combination {Combination} (hotkey {HotkeyId})",
                    binding.Combination,
                    binding.ConfigId);
                registrationId++;
                continue;
            }

            if (_registration.TryRegister(binding.RegistrationId, binding.Modifiers, binding.VirtualKey))
            {
                _active[binding.RegistrationId] = binding;
            }
            else
            {
                // RF-059/M2-RF-591: log the full combination, skip, continue; no retry.
                refused.Add(binding.Combination);
                _logger.Warning(
                    "Hotkeys: registration failed for {Combination} (hotkey {HotkeyId}); the combination is likely already in use",
                    binding.Combination,
                    binding.ConfigId);
            }

            registrationId++;
        }

        _logger.Information(
            "Hotkeys: applied. Registered={RegisteredCount} Refused={RefusedCount} Invalid={InvalidCount} Duplicate={DuplicateCount} Configured={ConfiguredCount}",
            _active.Count,
            refused.Count,
            invalidCount,
            duplicateCount,
            hotkeys.Count);

        if (refused.Count > 0)
        {
            // M2-RF-592: exactly one aggregated notification, never one balloon per failure.
            _notifier.Notify(
                "TouchPanel",
                BuildFailureMessage(refused),
                NotificationSeverity.Warning);
        }
    }

    private void EnsureSubscribed()
    {
        if (_subscribed)
        {
            return;
        }

        _registration.Pressed += OnPressed;
        _subscribed = true;
    }

    private void UnregisterAllCore()
    {
        foreach (var id in _active.Keys)
        {
            if (!_registration.TryUnregister(id))
            {
                _logger.Warning("Hotkeys: the operating system refused to unregister hotkey id {HotkeyRegistrationId}", id);
            }
        }

        _active.Clear();
    }

    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        if (_subscribed)
        {
            _registration.Pressed -= OnPressed;
            _subscribed = false;
        }

        UnregisterAllCore();

        try
        {
            _registration.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hotkeys: disposing the registration boundary failed");
        }

        _disposed = true;
    }

    private void OnPressed(object? sender, HotKeyPressedEventArgs e)
    {
        if (_disposed || !_active.TryGetValue(e.Id, out var binding))
        {
            return;
        }

        // Fire-and-forget: the executor contains every failure (RF-050/RF-583); the message pump
        // must never block, and the panel/foreground must never change (M2-RF-585).
        _ = DispatchAsync(binding);
    }

    private async Task DispatchAsync(HotKeyBinding binding)
    {
        try
        {
            await _executor
                .ExecuteAsync(binding.Action, binding.ConfigId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The executor boundary never throws; this guards a substituted implementation.
            _logger.Warning(ex, "Hotkeys: dispatching {Combination} failed", binding.Combination);
        }
    }

    private static string BuildFailureMessage(IReadOnlyList<string> refused)
    {
        if (refused.Count == 1)
        {
            return $"The global hotkey {refused[0]} could not be registered because its combination is already in use by another application.";
        }

        return $"{refused.Count} global hotkeys could not be registered because their combinations are already in use: {string.Join(", ", refused)}.";
    }
}
