namespace TouchPanel.Config.Migrations;

/// <summary>
/// Ordered, additive schema v1 → v2 migration (MD-1, RF-076, M2-RF-825). It introduces the M2
/// configuration shape without removing or renaming any v1 field: the <c>telemetry</c> section
/// (with <c>pollIntervalSeconds</c>), the top-level <c>hotkeys</c> list, the <c>sendKeys</c> action
/// discriminator, the Slider/Sparkline widget shapes, and the Toggle <c>persistState</c> flag.
/// </summary>
/// <remarks>
/// The concrete M2 fields default to their DTO defaults, so a v1 file deserializes directly into a
/// valid v2 model; this step only guarantees the documented defaults and raises the version. No
/// I/O is performed: <see cref="MigrationRunner"/> validates the result and the store persists it
/// atomically while rotating the original into the backup (RF-075.2/RF-078).
/// </remarks>
public sealed class V1ToV2Migration : IConfigMigration
{
    /// <inheritdoc/>
    public int FromVersion => 1;

    /// <inheritdoc/>
    public int ToVersion => 2;

    /// <inheritdoc/>
    public PanelConfig Migrate(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // RF-071/M2-RF-711: the interval defaults to 1 s (M1 cadence) when absent or nonsensical.
        if (!double.IsFinite(config.Telemetry.PollIntervalSeconds)
            || config.Telemetry.PollIntervalSeconds <= 0)
        {
            config.Telemetry.PollIntervalSeconds = TelemetryConfig.DefaultPollIntervalSeconds;
        }

        // RF-045/M2-RF-451: toggle persistence is opt-in; the DTO default is false (preserving
        // D-15), and a v1 Toggle has no persistState field, so no per-widget change is required.
        // The hotkeys list and the new widget/action shapes are likewise additive and default-empty.

        config.SchemaVersion = ToVersion;
        return config;
    }
}
