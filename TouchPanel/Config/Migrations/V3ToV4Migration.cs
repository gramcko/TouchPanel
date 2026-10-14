namespace TouchPanel.Config.Migrations;

/// <summary>
/// Ordered, additive schema v3 → v4 migration (M3-RF-846): it introduces the global
/// <c>appearance</c> section — <c>theme</c>, <c>accentColor</c>, and <c>iconPack</c> — without
/// removing or renaming any v3 field.
/// </summary>
/// <remarks>
/// <para>The new section is optional and its DTO defaults are the documented values
/// (<c>dark</c> / <c>null</c> / <c>default</c>), so a v3 file deserializes directly into a valid v4
/// model; no per-widget rewrite is required. The step only documents the additive shape and raises
/// the version. No I/O is performed: <see cref="MigrationRunner"/> validates the result and the
/// store persists it atomically while rotating the original into the backup
/// (RF-075.2/RF-078).</para>
/// <para>New widget-type discriminators (RF-046) are additive <c>type</c> values within the existing
/// <c>widgets[]</c> structure and therefore require no schema step (M3-RF-469).</para>
/// <para>The step is deterministic and idempotent: running it over its own output changes nothing
/// observable beyond the already-current schema version.</para>
/// </remarks>
public sealed class V3ToV4Migration : IConfigMigration
{
    /// <inheritdoc/>
    public int FromVersion => 3;

    /// <inheritdoc/>
    public int ToVersion => 4;

    /// <inheritdoc/>
    public PanelConfig Migrate(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // M3-RF-841/M3-RF-846: a v3 file has no appearance section, so the DTO defaults are already
        // the documented values (dark / null / default); this step only raises the version.
        config.SchemaVersion = ToVersion;
        return config;
    }
}
