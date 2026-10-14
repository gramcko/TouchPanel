namespace TouchPanel.Config.Migrations;

/// <summary>
/// Outcome of <see cref="MigrationRunner.Migrate"/>: the migrated and validated configuration and
/// the diagnostics gathered while migrating and validating it.
/// </summary>
public sealed record MigrationResult
{
    /// <summary>Gets the migrated configuration, already normalized by validation (RF-076).</summary>
    public required PanelConfig Config { get; init; }

    /// <summary>Gets the schema version the migration started from.</summary>
    public required int FromVersion { get; init; }

    /// <summary>Gets the schema version reached; always <see cref="MigrationRunner.CurrentVersion"/>.</summary>
    public required int ToVersion { get; init; }

    /// <summary>Gets the migration and validation diagnostics; never <see langword="null"/>.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; init; } = [];
}
