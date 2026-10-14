using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Startup;

namespace TouchPanel.Services.Config;

/// <summary>
/// Owns the single <see cref="SettingsCoordinator"/> shared by every settings-family editor surface
/// (the settings window, the hotkey/appearance editor, and the monitor picker) (T-17/RF-092,
/// M3-RF-615, M2-RF-922).
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="SettingsCoordinator"/> serializes its own read-modify-write-apply sequence with a
/// private gate. If two editor surfaces each built their own coordinator over the same running
/// configuration, overlapping edits could still build from the same pre-apply snapshot and the last
/// save would silently drop the other edit's section (the "hotkeys lost" symptom class). Caching one
/// coordinator here gives every settings-family write path a single serialization boundary.
/// </para>
/// <para>
/// WPF-free by construction; the composition root supplies the running-config accessors and the
/// shared apply pipeline. The store and applier are fixed for the process lifetime, while the
/// running configuration is re-read on every apply through the supplied accessor.
/// </para>
/// </remarks>
internal sealed class SettingsCoordinatorProvider
{
    private readonly Func<PanelConfig?> _configAccessor;
    private readonly IConfigStore _store;
    private readonly PanelConfigApplier _applier;
    private readonly Func<IStartupRegistration> _startupRegistrationFactory;
    private readonly Action<PanelConfig> _configSetter;
    private readonly ILogger? _logger;
    private readonly object _sync = new();
    private SettingsCoordinator? _coordinator;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsCoordinatorProvider"/> class.
    /// </summary>
    /// <param name="configAccessor">Returns the currently running configuration (re-read on every apply).</param>
    /// <param name="store">The single-writer store the candidate is persisted through.</param>
    /// <param name="applier">The shared apply pipeline used after a successful save (MD-10).</param>
    /// <param name="startupRegistrationFactory">Creates the per-user start-with-Windows boundary; invoked once, lazily.</param>
    /// <param name="configSetter">Publishes the validated configuration as the running one.</param>
    /// <param name="logger">Logger for path/summary-only messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public SettingsCoordinatorProvider(
        Func<PanelConfig?> configAccessor,
        IConfigStore store,
        PanelConfigApplier applier,
        Func<IStartupRegistration> startupRegistrationFactory,
        Action<PanelConfig> configSetter,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configAccessor);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(startupRegistrationFactory);
        ArgumentNullException.ThrowIfNull(configSetter);

        _configAccessor = configAccessor;
        _store = store;
        _applier = applier;
        _startupRegistrationFactory = startupRegistrationFactory;
        _configSetter = configSetter;
        _logger = logger;
    }

    /// <summary>
    /// Gets the single settings boundary shared by all settings-family editors, creating it on first
    /// use. The same instance is returned for every caller so overlapping edits are serialized by the
    /// coordinator's one apply gate (M3-RF-615/M2-RF-922).
    /// </summary>
    /// <returns>The shared settings-apply boundary; never <see langword="null"/>.</returns>
    public ISettingsService Get()
    {
        var existing = _coordinator;
        if (existing is not null)
        {
            return existing;
        }

        lock (_sync)
        {
            return _coordinator ??= new SettingsCoordinator(
                _configAccessor,
                _store,
                _applier,
                _startupRegistrationFactory(),
                _configSetter,
                _logger);
        }
    }
}
