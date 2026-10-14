namespace TouchPanel.Config.Migrations;

/// <summary>
/// The production ordered migration chain for <c>panel.json</c> (MD-1). Kept separate from
/// <see cref="MigrationRunner"/> so the runner stays a generic engine and tests can inject their
/// own steps, while the store picks up the real chain by default.
/// </summary>
public static class ConfigMigrationChain
{
    /// <summary>
    /// Creates the ordered steps from the oldest supported schema to
    /// <see cref="PanelConfig.CurrentSchemaVersion"/>. A new instance is returned per call.
    /// </summary>
    /// <returns>The production migration steps.</returns>
    public static IReadOnlyList<IConfigMigration> Create() =>
    [
        new V1ToV2Migration(),
        new V2ToV3Migration(),
        new V3ToV4Migration(),
    ];
}
