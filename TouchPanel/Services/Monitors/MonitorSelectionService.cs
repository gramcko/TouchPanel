using Serilog;
using TouchPanel.Config;

namespace TouchPanel.Services.Monitors;

/// <summary>
/// Result of one monitor-selection evaluation (RF-016/RF-017). <see cref="Monitor"/> is the monitor
/// the panel should occupy (the configured target, or the primary when the configured fallback is
/// used); it is <see langword="null"/> when the panel must stay hidden.
/// </summary>
public sealed record MonitorSelectionResult
{
    /// <summary>
    /// Gets the monitor the panel should occupy, or <see langword="null"/> when the panel must stay
    /// hidden (RF-017 with fallback <c>none</c>, or no monitor at all).
    /// </summary>
    public MonitorDescriptor? Monitor { get; init; }

    /// <summary>
    /// Gets a value indicating whether the configured target monitor itself was found. When this is
    /// <see langword="false"/> but <see cref="Monitor"/> is not <see langword="null"/>, the primary
    /// fallback was used and the placement must be accompanied by a notification (RF-017).
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Monitor"/> is the primary fallback rather than the
    /// configured target. Fallback placement is never silent (RF-017).
    /// </summary>
    public bool UsedFallback { get; init; }

    /// <summary>Gets the effective fallback policy (an unknown configured value is treated as <see cref="MonitorFallback.None"/>).</summary>
    public MonitorFallback Fallback { get; init; }

    /// <summary>Gets the human-readable reason for the outcome, suitable for logs and notifications.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the panel has a monitor to occupy.</summary>
    public bool CanShow => Monitor is not null;
}

/// <summary>
/// Combines the <see cref="IMonitorEnumerator"/> boundary, the pure <see cref="MonitorMatcher"/> and
/// the configured <see cref="MonitorConfig"/> into the RF-017 decision T-08 acts on: a resolved
/// target descriptor, or "unavailable" plus the configured fallback policy and a reason. Logs every
/// discovered monitor once per distinct set with its identity fields (D-17) and logs the chosen
/// match or the unavailability with the reason (RF-008/RF-016).
/// </summary>
/// <remarks>
/// <para><b>No polling.</b> Availability is evaluated only when <see cref="Select"/> is called;
/// T-08 calls it at startup and on display-change events (RF-022). The service keeps no timers and
/// subscribes to nothing.</para>
/// <para><b>Fallback.</b> With <see cref="MonitorFallback.None"/> an unavailable target yields
/// <see cref="MonitorSelectionResult.Monitor"/> = <see langword="null"/> (stay hidden, D-2). With
/// <see cref="MonitorFallback.Primary"/> the primary monitor is returned with
/// <see cref="MonitorSelectionResult.UsedFallback"/> = <see langword="true"/> so the caller can
/// notify the user; the fallback is never silent (RF-017).</para>
/// </remarks>
public sealed class MonitorSelectionService
{
    private readonly IMonitorEnumerator _enumerator;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private string? _loggedDiscoverySignature;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorSelectionService"/> class.
    /// </summary>
    /// <param name="enumerator">The monitor discovery boundary (production: <c>Win32MonitorEnumerator</c>).</param>
    /// <param name="logger">Logger for discovery/selection entries; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="enumerator"/> is <see langword="null"/>.</exception>
    public MonitorSelectionService(IMonitorEnumerator enumerator, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        _enumerator = enumerator;
        _logger = (logger ?? Serilog.Log.Logger).ForContext<MonitorSelectionService>();
    }

    /// <summary>
    /// Evaluates the configured target monitor against the currently connected monitors and applies
    /// the configured fallback policy (RF-016/RF-017). Never throws: enumeration failures are logged
    /// and produce an unavailable result.
    /// </summary>
    /// <param name="config">The monitor section of the loaded configuration.</param>
    /// <returns>The selection result; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public MonitorSelectionResult Select(MonitorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var fallback = NormalizeFallback(config.Fallback);

        IReadOnlyList<MonitorDescriptor> monitors;
        try
        {
            monitors = _enumerator.Enumerate() ?? [];
        }
        catch (Exception ex)
        {
            // The enumerator is expected to absorb native failures; this guards the boundary so a
            // selection never takes the application down (RF-012/RNF-013).
            _logger.Error(ex, "Monitor selection: enumeration failed; the target monitor is treated as unavailable");
            return Unavailable(fallback, "monitor enumeration failed");
        }

        LogDiscoveredMonitors(monitors);

        var match = MonitorMatcher.Match(config.Match, monitors);
        if (match.IsMatch)
        {
            _logger.Information(
                "Monitor selection: target monitor resolved. Monitor={Monitor} SourceGdiName={SourceGdiName} DevicePath={DevicePath} HardwareId={HardwareId} Criterion={Criterion} Reason={Reason}",
                match.Monitor!.DisplayName,
                match.Monitor.SourceGdiName,
                match.Monitor.DevicePath,
                match.Monitor.HardwareIdWithSerial,
                match.Criterion,
                match.Reason);

            return new MonitorSelectionResult
            {
                Monitor = match.Monitor,
                IsAvailable = true,
                UsedFallback = false,
                Fallback = fallback,
                Reason = match.Reason,
            };
        }

        _logger.Warning(
            "Monitor selection: configured target monitor is unavailable. Reason={Reason} Fallback={Fallback} DetectedMonitors={DetectedMonitors}",
            match.Reason,
            fallback,
            monitors.Count);

        if (fallback == MonitorFallback.Primary)
        {
            var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary);
            if (primary is not null)
            {
                var reason = $"configured target monitor unavailable ({match.Reason}); using the configured primary fallback";
                _logger.Warning(
                    "Monitor selection: falling back to the primary monitor as configured (a user notification is required). Monitor={Monitor} SourceGdiName={SourceGdiName} Reason={Reason}",
                    primary.DisplayName,
                    primary.SourceGdiName,
                    reason);

                return new MonitorSelectionResult
                {
                    Monitor = primary,
                    IsAvailable = false,
                    UsedFallback = true,
                    Fallback = fallback,
                    Reason = reason,
                };
            }

            _logger.Warning("Monitor selection: the primary fallback is configured but no primary monitor was detected");
            return Unavailable(fallback, $"{match.Reason}; the primary fallback is configured but no primary monitor was detected");
        }

        return Unavailable(fallback, match.Reason);
    }

    private static MonitorFallback NormalizeFallback(MonitorFallback fallback) =>
        fallback == MonitorFallback.Primary ? MonitorFallback.Primary : MonitorFallback.None;

    private static MonitorSelectionResult Unavailable(MonitorFallback fallback, string reason) => new()
    {
        Monitor = null,
        IsAvailable = false,
        UsedFallback = false,
        Fallback = fallback,
        Reason = reason,
    };

    /// <summary>
    /// Logs every discovered monitor with its identity fields (D-17). The full list is logged once
    /// per distinct set: repeated evaluations with the same monitors (for example the display-change
    /// handler re-checking after an unrelated event) do not duplicate the discovery entries, while a
    /// changed set (monitor connected/disconnected) is logged again.
    /// </summary>
    private void LogDiscoveredMonitors(IReadOnlyList<MonitorDescriptor> monitors)
    {
        var signature = BuildDiscoverySignature(monitors);
        lock (_gate)
        {
            if (string.Equals(signature, _loggedDiscoverySignature, StringComparison.Ordinal))
            {
                return;
            }

            _loggedDiscoverySignature = signature;
        }

        _logger.Information("Monitor discovery: {Count} active monitor(s) detected", monitors.Count);

        for (var index = 0; index < monitors.Count; index++)
        {
            var monitor = monitors[index];
            _logger.Information(
                "Monitor discovered. Index={Index} GdiName={GdiName} FriendlyName={FriendlyName} Primary={Primary} Bounds={Bounds} WorkArea={WorkArea} HardwareId={HardwareId} HardwareIdWithSerial={HardwareIdWithSerial} Manufacturer={Manufacturer} ProductCode={ProductCode} Serial={Serial} DevicePath={DevicePath} MonitorDeviceId={MonitorDeviceId}",
                index,
                monitor.SourceGdiName,
                monitor.FriendlyName,
                monitor.IsPrimary,
                monitor.Bounds,
                monitor.WorkArea,
                monitor.HardwareId,
                monitor.HardwareIdWithSerial,
                monitor.Manufacturer,
                monitor.ProductCode,
                monitor.SerialNumber,
                monitor.DevicePath,
                monitor.MonitorDeviceId);
        }
    }

    private static string BuildDiscoverySignature(IReadOnlyList<MonitorDescriptor> monitors) =>
        string.Join(
            '\n',
            monitors
                .Select(monitor =>
                    $"{monitor.SourceGdiName}|{monitor.DevicePath}|{monitor.MonitorDeviceId}|{monitor.HardwareIdWithSerial}|{monitor.FriendlyName}|{monitor.Bounds}|{monitor.IsPrimary}")
                .OrderBy(key => key, StringComparer.Ordinal));
}
