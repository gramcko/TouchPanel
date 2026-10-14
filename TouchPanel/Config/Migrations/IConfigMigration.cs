namespace TouchPanel.Config.Migrations;

/// <summary>
/// One ordered, idempotent migration step between two adjacent schema versions (RF-076).
/// Migrations transform a deserialized <see cref="PanelConfig"/> in place and must never perform
/// I/O: the caller owns persistence so a failed migration can never overwrite the source file.
/// </summary>
/// <remarks>
/// Implementations run on data that was already parsed with the current model set, so a migration
/// mainly maps old meanings onto the v1 shape (renames, defaults, reshapes). Any exception thrown
/// by <see cref="Migrate"/> is wrapped in <see cref="ConfigMigrationException"/> by
/// <see cref="MigrationRunner"/> and routes the load through the invalid/corrupt path (RF-075.3,
/// RF-076), leaving the source file untouched.
/// </remarks>
public interface IConfigMigration
{
    /// <summary>Gets the schema version this step reads.</summary>
    int FromVersion { get; }

    /// <summary>Gets the schema version this step produces; must be greater than <see cref="FromVersion"/>.</summary>
    int ToVersion { get; }

    /// <summary>
    /// Applies the migration to <paramref name="config"/> and returns it (the same instance is
    /// acceptable). Implementations are expected to be deterministic and idempotent for a given
    /// input and source version.
    /// </summary>
    /// <param name="config">The configuration to migrate.</param>
    /// <returns>The migrated configuration.</returns>
    PanelConfig Migrate(PanelConfig config);
}
