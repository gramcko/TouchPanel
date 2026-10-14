namespace TouchPanel.Config.Migrations;

/// <summary>
/// Ordered, additive schema v2 → v3 migration (RF-076, RF-043/M2-RF-435/M2-RF-436). It introduces
/// the per-widget appearance override fields — <c>icon</c>, <c>foreground</c>, <c>background</c>,
/// <c>accent</c>, <c>fontSize</c>, and <c>opacity</c> — without removing or renaming any v2 field.
/// </summary>
/// <remarks>
/// <para>The new fields are optional and default to <see langword="null"/> ("use the theme default"),
/// so a v2 file deserializes directly into a valid v3 model; no per-widget rewrite is required. The
/// step only documents the additive shape and raises the version. No I/O is performed:
/// <see cref="MigrationRunner"/> validates the result and the store persists it atomically while
/// rotating the original into the backup (RF-075.2/RF-078).</para>
/// <para>The step is deterministic and idempotent: running it over its own output changes nothing
/// observable beyond the already-current schema version.</para>
/// </remarks>
public sealed class V2ToV3Migration : IConfigMigration
{
    /// <inheritdoc/>
    public int FromVersion => 2;

    /// <inheritdoc/>
    public int ToVersion => 3;

    /// <inheritdoc/>
    public PanelConfig Migrate(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // RF-043/M2-RF-435/M2-RF-436: appearance overrides are opt-in overrides layered over the
        // theme (RFC: null = theme default). A v2 widget has none, so the nullable DTO defaults are
        // already the documented values; this step only raises the schema version.
        config.SchemaVersion = ToVersion;
        return config;
    }
}
