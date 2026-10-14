using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Services.Time;

namespace TouchPanel.Widgets;

/// <summary>
/// The dependencies every widget view model is built with (DD-6): the action boundary the
/// interactive widgets execute through (T-09), the snapshot boundary data-driven widgets read
/// (T-11/RF-062), the clock for the ~2 s error state (RF-036), the UI-thread marshalling
/// delegate, and the logger. One context is created per panel by the composition root and shared
/// by all registrations.
/// </summary>
public sealed record WidgetCreationContext
{
    /// <summary>
    /// Gets the id of the page currently being built (RF-045/M2-RF-452). It scopes persisted toggle
    /// state to the composite key <c>"&lt;pageId&gt;/&lt;widgetId&gt;"</c>. The registry sets it per
    /// page through a <c>with</c> copy so the shared context stays page-independent.
    /// </summary>
    public string? PageId { get; init; }

    /// <summary>
    /// Gets the persisted-toggle lookup consumed by the Toggle view model (RF-045): given
    /// <c>(pageId, widgetId)</c> it returns the stored on/off state, or <see langword="null"/> when
    /// none was persisted. <see langword="null"/> disables persistence (tests, M1 behavior).
    /// </summary>
    public Func<string, string, bool?>? ToggleStateLookup { get; init; }

    /// <summary>
    /// Gets the persisted-toggle write seam consumed by the Toggle view model (RF-045): given
    /// <c>(pageId, widgetId, isOn)</c> it records the new state and schedules a debounced
    /// <c>state.json</c> save. <see langword="null"/> leaves flips runtime-only (M1/D-15).
    /// </summary>
    public Action<string, string, bool>? ToggleStateChanged { get; init; }

    /// <summary>Gets the action executor bound to interactive widgets; <see langword="null"/> leaves actions inert.</summary>
    public IActionExecutor? ActionExecutor { get; init; }

    /// <summary>
    /// Gets the application-level snapshot source consumed by data-driven widgets such as the
    /// Gauge; <see langword="null"/> leaves them permanently unavailable (tests, degraded startup).
    /// The Sparkline also uses it purely as a change signal (M2-RF-412).
    /// </summary>
    public ISensorSnapshotSource? SnapshotSource { get; init; }

    /// <summary>
    /// Gets the read-only bounded telemetry history consumed by the Sparkline (RF-041/M2-RF-412);
    /// <see langword="null"/> leaves history-driven widgets permanently unavailable.
    /// </summary>
    public ISensorHistory? History { get; init; }

    /// <summary>
    /// Gets the seam a history-consuming widget uses to request a buffer for its metric through the
    /// existing monitoring-owned tracking surface (M2-RF-693). The composition root bridges it to
    /// <c>SensorHistoryStore.Track</c>; configuration reconciliation by the apply pipeline remains
    /// the authoritative tracked-metric set (T-11). <see langword="null"/> skips the request.
    /// </summary>
    public Action<string>? RequestHistoryTracking { get; init; }

    /// <summary>Gets the clock used for widget error-state timing; defaults to <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Gets the OS clock/state seam consumed by the Clock widget; <see langword="null"/> disables event-driven recomputation.</summary>
    public IClockStateSource? ClockStateSource { get; init; }

    /// <summary>
    /// Gets the canonical Clock display-culture source (M4-RF-951) supplied to the Clock widget so
    /// runtime formatting uses the same culture as load-time validation; <see langword="null"/> uses
    /// <see cref="ClockCultureSource.Default"/> (tests/M1 behavior).
    /// </summary>
    public IClockCultureSource? ClockCulture { get; init; }

    /// <summary>
    /// Gets the application-level appearance source consumed by every widget view model to resolve
    /// its icon through the active icon pack and refresh live on a pack change (M3-RF-844/M3-RF-845).
    /// <see langword="null"/> leaves icons resolving through the default pack (tests/M1 behavior).
    /// </summary>
    public IAppearanceSource? AppearanceSource { get; init; }

    /// <summary>Gets the delegate that marshals view-model state changes to the UI thread; <see langword="null"/> runs inline (tests).</summary>
    public Action<Action>? Dispatch { get; init; }

    /// <summary>Gets the logger for widget-level failures; <see langword="null"/> uses the global Serilog logger.</summary>
    public ILogger? Logger { get; init; }
}
