using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Services.Startup;

namespace TouchPanel.Services.Config;

/// <summary>
/// One validated set of settings edits (T-17, M2-RF-921/M2-RF-932): the window, startup, telemetry,
/// and monitor values a settings surface wants to apply. Every property is optional; a
/// <see langword="null"/> value leaves the corresponding running value unchanged. <see
/// cref="SetMonitorMatch"/> distinguishes "leave the monitor identity untouched" from "write the
/// supplied identity (which may be <see langword="null"/> to clear it)".
/// </summary>
public sealed record SettingsChange
{
    /// <summary>Gets the desired <c>window.alwaysOnTop</c> value, or <see langword="null"/> to leave it unchanged.</summary>
    public bool? AlwaysOnTop { get; init; }

    /// <summary>
    /// Gets the desired <c>window.pageTransition</c> id (user-requested addition), or
    /// <see langword="null"/> to leave it unchanged. The value is normalized by validation, so an
    /// unknown id falls back to <c>slide</c> with a warning and never fails the whole change.
    /// </summary>
    public string? PageTransition { get; init; }

    /// <summary>Gets the desired <c>startup.startWithWindows</c> value, or <see langword="null"/> to leave it unchanged.</summary>
    public bool? StartWithWindows { get; init; }

    /// <summary>Gets the desired <c>telemetry.pollIntervalSeconds</c> value, or <see langword="null"/> to leave it unchanged.</summary>
    public double? PollIntervalSeconds { get; init; }

    /// <summary>Gets a value indicating whether <see cref="MonitorMatch"/> should be written to <c>monitor.match</c>.</summary>
    public bool SetMonitorMatch { get; init; }

    /// <summary>Gets the monitor identity block to store, or <see langword="null"/> to clear <c>monitor.match</c> when <see cref="SetMonitorMatch"/> is set.</summary>
    public MonitorIdentity? MonitorMatch { get; init; }

    /// <summary>Gets the desired <c>monitor.fallback</c> policy, or <see langword="null"/> to leave it unchanged.</summary>
    public MonitorFallback? MonitorFallback { get; init; }

    /// <summary>
    /// Gets the desired full <c>hotkeys</c> list, or <see langword="null"/> to leave it unchanged
    /// (M3-RF-615). A non-null value replaces the whole list; every other section is preserved by
    /// <see cref="SettingsCoordinator.BuildUpdated"/>.
    /// </summary>
    public IReadOnlyList<HotkeyConfig>? Hotkeys { get; init; }

    /// <summary>
    /// Gets the desired global <c>appearance</c> section, or <see langword="null"/> to leave it
    /// unchanged (M3-RF-841). A non-null value replaces the whole section; every other section
    /// (including <c>hotkeys</c>) is preserved by <see cref="SettingsCoordinator.BuildUpdated"/> so
    /// an appearance edit never resets hotkeys (and vice versa).
    /// </summary>
    public AppearanceConfig? Appearance { get; init; }

    /// <summary>
    /// Gets the desired full <c>pages</c> list, or <see langword="null"/> to leave the running page
    /// list unchanged (user-requested pages management). A non-null value replaces the page list
    /// atomically; every other section is preserved, and the shared apply pipeline rebuilds the
    /// active page. The caller is responsible for the last-page guard and unique ids
    /// (<see cref="PageListOperations"/>).
    /// </summary>
    public IReadOnlyList<PageConfig>? Pages { get; init; }

    /// <summary>
    /// Gets the desired <c>activePageId</c>, or <see langword="null"/> to leave it unchanged.
    /// Removing the active page passes the deterministic first-page fallback from
    /// <see cref="PageListOperations.RemovePage"/>; validation also substitutes an unknown id.
    /// </summary>
    public string? ActivePageId { get; init; }
}

/// <summary>
/// Outcome of a settings apply (T-17, M2-RF-922). <see cref="Succeeded"/> is only
/// <see langword="true"/> when the change validated, persisted atomically, and was applied through
/// the shared pipeline; every other outcome leaves the running configuration untouched.
/// </summary>
public sealed record SettingsApplyResult
{
    /// <summary>Gets a value indicating whether the settings were applied.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets the applied configuration when <see cref="Succeeded"/> is <see langword="true"/>; otherwise <see langword="null"/>.</summary>
    public PanelConfig? Config { get; init; }

    /// <summary>Gets a stable machine-readable failure code, or <see langword="null"/> on success.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets a notification-safe failure description, or <see langword="null"/> on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Creates a failure result.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Notification-safe failure description.</param>
    /// <returns>The result.</returns>
    public static SettingsApplyResult Failure(string code, string message) =>
        new() { Succeeded = false, ErrorCode = code, ErrorMessage = message };

    /// <summary>Creates a success result carrying the applied configuration.</summary>
    /// <param name="config">The configuration that was applied.</param>
    /// <returns>The result.</returns>
    public static SettingsApplyResult Success(PanelConfig config) =>
        new() { Succeeded = true, Config = config };
}

/// <summary>
/// Boundary the settings and monitor-picker view models drive (T-17, M2-RF-921..925/M2-RF-931..934).
/// The production implementation is <see cref="SettingsCoordinator"/>; view models depend only on
/// this contract so validation, persistence, and apply intent are testable without WPF (M2-RNF-124).
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Validates one set of settings edits, persists the result atomically through the single-writer
    /// store, and applies it through the shared apply pipeline (M2-RF-922/M2-RF-923). A start-with-
    /// Windows change is applied through the existing M1 registration first.
    /// </summary>
    /// <param name="change">The requested edits; must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the persistence write and the registration attempt.</param>
    /// <returns>The apply outcome; never <see langword="null"/>.</returns>
    Task<SettingsApplyResult> ApplyAsync(SettingsChange change, CancellationToken cancellationToken = default);
}

/// <summary>
/// Settings-apply orchestration (T-17, RF-092/M2-RF-921..925, MD-15). It builds a candidate
/// configuration from the running one (changing only the requested fields, never mutating the
/// running instance before the save succeeds), normalizes it with the exact rules
/// <see cref="ConfigValidator"/> uses (M2-RF-922), applies the per-user start-with-Windows
/// registration (RF-003/RF-004), persists the candidate atomically through
/// <see cref="IConfigStore"/> (RF-077), and only then publishes it as the running configuration and
/// applies it through the shared <see cref="PanelConfigApplier"/> (MD-10/M2-RF-923). A failure at
/// any step leaves the running configuration untouched (M2-RF-922).
/// </summary>
/// <remarks>
/// WPF-free by construction; the apply target owns UI-thread marshalling (MD-10). Only paths and
/// result summaries are logged, never configuration content (RF-085).
/// </remarks>
public sealed class SettingsCoordinator : ISettingsService
{
    private readonly Func<PanelConfig?> _configAccessor;
    private readonly IConfigStore _store;
    private readonly PanelConfigApplier _applier;
    private readonly IStartupRegistration _startupRegistration;
    private readonly Action<PanelConfig> _configSetter;
    private readonly ILogger _logger;

    /// <summary>
    /// Serializes the whole read-modify-write-apply sequence. The store guards only the physical
    /// write, so two overlapping applies built from the same pre-apply snapshot would each replace
    /// the whole root and the last save would silently drop the other edit's section
    /// (M3-RF-615/M2-RF-922).
    /// </summary>
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsCoordinator"/> class.
    /// </summary>
    /// <param name="configAccessor">Returns the currently running configuration (re-read on every apply).</param>
    /// <param name="store">The single-writer store the candidate is persisted through.</param>
    /// <param name="applier">The shared apply pipeline used after a successful save (MD-10).</param>
    /// <param name="startupRegistration">The M1 per-user start-with-Windows boundary (RF-003/RF-004).</param>
    /// <param name="configSetter">Publishes the validated configuration as the running one.</param>
    /// <param name="logger">Logger for path/summary-only messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public SettingsCoordinator(
        Func<PanelConfig?> configAccessor,
        IConfigStore store,
        PanelConfigApplier applier,
        IStartupRegistration startupRegistration,
        Action<PanelConfig> configSetter,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configAccessor);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(startupRegistration);
        ArgumentNullException.ThrowIfNull(configSetter);

        _configAccessor = configAccessor;
        _store = store;
        _applier = applier;
        _startupRegistration = startupRegistration;
        _configSetter = configSetter;
        _logger = (logger ?? Log.Logger).ForContext<SettingsCoordinator>();
    }

    /// <inheritdoc />
    public async Task<SettingsApplyResult> ApplyAsync(
        SettingsChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        try
        {
            await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Settings: the change was canceled before it was applied");
            return SettingsApplyResult.Failure("settings.canceled", "The change was canceled; nothing changed.");
        }

        try
        {
            return await ApplyCoreAsync(change, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    /// <summary>
    /// Builds, validates, persists, and applies one settings change. Runs under <see cref="_applyGate"/>
    /// so the candidate is always built from the latest running configuration (a concurrent apply has
    /// already published its result before the gate is released).
    /// </summary>
    private async Task<SettingsApplyResult> ApplyCoreAsync(
        SettingsChange change,
        CancellationToken cancellationToken)
    {
        var current = _configAccessor();
        if (current is null)
        {
            _logger.Warning("Settings: no running configuration is available; the change was not applied");
            return SettingsApplyResult.Failure("settings.noConfig", "No configuration is available to change.");
        }

        var updated = BuildUpdated(current, change);

        // M2-RF-922: the same normalization rules as the config validator (clamps telemetry into
        // 0.5–5 s, replaces unknown enum sentinels). The candidate is normalized before it is saved.
        ConfigValidator.Validate(updated);

        if (change.StartWithWindows is bool startWithWindows)
        {
            var registration = await ApplyStartupRegistrationAsync(startWithWindows, cancellationToken).ConfigureAwait(false);
            if (registration is not null)
            {
                return registration;
            }
        }

        ConfigSaveResult save;
        try
        {
            save = await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Settings: the change was canceled before it was saved");
            return SettingsApplyResult.Failure("settings.canceled", "The change was canceled; nothing changed.");
        }
        catch (Exception ex)
        {
            // The store contract returns failures; this guards a custom implementation that throws.
            _logger.Error(ex, "Settings: saving the change failed unexpectedly");
            return SettingsApplyResult.Failure("settings.saveFailed", "The settings could not be saved; nothing changed.");
        }

        if (!save.Succeeded)
        {
            _logger.Error("Settings: the change could not be saved; the running configuration is kept");
            return SettingsApplyResult.Failure(
                "settings.saveFailed",
                save.Diagnostics.Count > 0 && !string.IsNullOrWhiteSpace(save.Diagnostics[0].Message)
                    ? save.Diagnostics[0].Message
                    : "The settings could not be saved; nothing changed.");
        }

        // M2-RF-922/M2-RF-923: only now is the candidate published and applied; the applier isolates
        // each subsystem step so one failing step cannot strand the others (RF-012).
        _configSetter(updated);
        try
        {
            _applier.Apply(updated);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Settings: applying the saved configuration failed; the remaining subsystems keep running");
        }

        _logger.Information("Settings: the change was saved and applied");
        return SettingsApplyResult.Success(updated);
    }

    /// <summary>
    /// Applies the M1 start-with-Windows registration off the UI thread (RF-004/RNF-004). Returns
    /// <see langword="null"/> on success (the change may continue) or a failure result when the
    /// registration could not be applied, in which case the configuration is not persisted so the
    /// stored value stays consistent with the actual registry state.
    /// </summary>
    private async Task<SettingsApplyResult?> ApplyStartupRegistrationAsync(
        bool startWithWindows,
        CancellationToken cancellationToken)
    {
        StartupRegistrationResult result;
        try
        {
            result = await Task.Run(() => _startupRegistration.Apply(startWithWindows), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SettingsApplyResult.Failure("settings.canceled", "The change was canceled; nothing changed.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Settings: applying the start-with-Windows registration failed unexpectedly");
            return SettingsApplyResult.Failure(
                "settings.startupFailed",
                "The start-with-Windows setting could not be applied; nothing changed.");
        }

        if (result.Succeeded)
        {
            return null;
        }

        _logger.Error(
            "Settings: the start-with-Windows registration failed. Code={Code}",
            result.FailureCode);
        return SettingsApplyResult.Failure(
            result.FailureCode ?? "settings.startupFailed",
            result.Message ?? "The start-with-Windows setting could not be applied; nothing changed.");
    }

    /// <summary>
    /// Builds the candidate configuration: a new root that shares the unchanged collections (pages,
    /// hotkeys) and sections (appearance) and replaces only the requested values. The running
    /// configuration is never mutated before the save succeeds, so a failed save cannot partially
    /// apply (M2-RF-922). Every section that is not explicitly requested keeps its running value, so
    /// a hotkey edit never resets appearance and an appearance edit never resets hotkeys
    /// (M3-RF-615/M3-RF-841).
    /// </summary>
    private static PanelConfig BuildUpdated(PanelConfig current, SettingsChange change)
    {
        var monitorIdentity = change.SetMonitorMatch ? change.MonitorMatch : current.Monitor.Match;
        var monitorFallback = change.MonitorFallback ?? current.Monitor.Fallback;

        // Pages management replaces the list and, when needed, the active id. A null value keeps the
        // running pages exactly as before, so unrelated settings edits never reorder or reset pages.
        var pages = change.Pages is { } requestedPages
            ? new List<PageConfig>(requestedPages)
            : current.Pages;

        return new PanelConfig
        {
            SchemaVersion = current.SchemaVersion,
            ActivePageId = change.ActivePageId ?? current.ActivePageId,
            Pages = pages,

            // M3-RF-615/M3-RF-841: a non-null Hotkeys/Appearance change replaces that section; a
            // null value preserves the running one. Every other section keeps its running value so
            // the two edits are independent.
            Hotkeys = change.Hotkeys is { } hotkeys ? new List<HotkeyConfig>(hotkeys) : current.Hotkeys,
            Appearance = change.Appearance is { } appearance ? appearance : current.Appearance,
            Window = new WindowConfig
            {
                AlwaysOnTop = change.AlwaysOnTop ?? current.Window.AlwaysOnTop,

                // User-requested page transitions: an explicit new id replaces the running one; null
                // preserves it, so unrelated edits never reset the animation.
                PageTransition = change.PageTransition ?? current.Window.PageTransition,
            },
            Startup = new StartupConfig
            {
                StartWithWindows = change.StartWithWindows ?? current.Startup.StartWithWindows,
            },
            Telemetry = new TelemetryConfig
            {
                PollIntervalSeconds = change.PollIntervalSeconds ?? current.Telemetry.PollIntervalSeconds,
            },
            Monitor = new MonitorConfig
            {
                Match = monitorIdentity,
                Fallback = monitorFallback,
            },
        };
    }
}
