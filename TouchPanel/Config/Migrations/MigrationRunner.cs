using TouchPanel.Config.Validation;
using TouchPanel.Services.Time;

namespace TouchPanel.Config.Migrations;

/// <summary>
/// Applies ordered <see cref="IConfigMigration"/> steps from an older schema version to the
/// current one, then validates the migrated result (RF-075.2, RF-076).
/// </summary>
/// <remarks>
/// <para>The runner is deterministic and idempotent: for the same input and <c>fromVersion</c> it
/// applies the same steps in the same order, and running it on an already-current configuration
/// performs no steps. Contiguity is checked while walking the chain; a gap or a failing step is
/// reported as <see cref="ConfigMigrationException"/>.</para>
/// <para>Validation happens through <see cref="ConfigValidator.Validate"/>, which normalizes the
/// migrated configuration in place and contributes its diagnostics to the result. The runner
/// performs no I/O; persistence and the invalid/corrupt recovery path belong to
/// <c>JsonConfigStore</c>, so a migration failure can never destroy the source file
/// (RF-076).</para>
/// </remarks>
public sealed class MigrationRunner
{
    private readonly Dictionary<int, IConfigMigration> _byFromVersion;
    private readonly IClockCultureSource? _clockCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="MigrationRunner"/> class.
    /// </summary>
    /// <param name="migrations">
    /// The available migration steps in any order; <see langword="null"/> means an empty chain
    /// (the v1 baseline, where no real migration exists yet).
    /// </param>
    /// <param name="currentVersion">The schema version this build understands; defaults to <see cref="PanelConfig.CurrentSchemaVersion"/>.</param>
    /// <param name="clockCulture">
    /// The canonical Clock culture source (M4-RF-951) threaded to the post-migration validation so
    /// a migrated custom Clock format is checked against the same display culture as runtime;
    /// <see langword="null"/> uses <see cref="ClockCultureSource.Default"/>.
    /// </param>
    public MigrationRunner(
        IEnumerable<IConfigMigration>? migrations = null,
        int currentVersion = PanelConfig.CurrentSchemaVersion,
        IClockCultureSource? clockCulture = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(currentVersion, 1);

        CurrentVersion = currentVersion;
        _clockCulture = clockCulture;
        _byFromVersion = new Dictionary<int, IConfigMigration>();

        var ordered = new List<IConfigMigration>();
        foreach (var migration in migrations ?? Enumerable.Empty<IConfigMigration>())
        {
            ArgumentNullException.ThrowIfNull(migration);

            if (migration.ToVersion <= migration.FromVersion)
            {
                throw new ArgumentException(
                    $"Migration from schema {migration.FromVersion} must target a greater version.",
                    nameof(migrations));
            }

            if (migration.ToVersion > currentVersion)
            {
                throw new ArgumentException(
                    $"Migration from schema {migration.FromVersion} targets schema {migration.ToVersion}, "
                    + $"which is newer than the current schema {currentVersion}.",
                    nameof(migrations));
            }

            if (!_byFromVersion.TryAdd(migration.FromVersion, migration))
            {
                throw new ArgumentException(
                    $"Duplicate migration from schema {migration.FromVersion}.",
                    nameof(migrations));
            }

            ordered.Add(migration);
        }

        ordered.Sort(static (left, right) => left.FromVersion.CompareTo(right.FromVersion));
        Migrations = ordered;
    }

    /// <summary>Gets the schema version this runner migrates to.</summary>
    public int CurrentVersion { get; }

    /// <summary>Gets the configured steps, ordered by <see cref="IConfigMigration.FromVersion"/>.</summary>
    public IReadOnlyList<IConfigMigration> Migrations { get; }

    /// <summary>
    /// Determines whether a complete migration chain exists from <paramref name="fromVersion"/> to
    /// <see cref="CurrentVersion"/>.
    /// </summary>
    /// <param name="fromVersion">The schema version of the configuration to migrate.</param>
    /// <returns><see langword="true"/> when <see cref="Migrate"/> can complete.</returns>
    public bool CanMigrateFrom(int fromVersion)
    {
        var version = fromVersion;
        var guard = 0;

        while (version < CurrentVersion)
        {
            if (!_byFromVersion.TryGetValue(version, out var step) || step.ToVersion <= version)
            {
                return false;
            }

            version = step.ToVersion;

            // Defensive bound: the constructor already guarantees forward progress, so a chain
            // longer than the version range means the runner was mutated or misconfigured.
            if (++guard > CurrentVersion)
            {
                return false;
            }
        }

        return version == CurrentVersion;
    }

    /// <summary>
    /// Migrates and validates a configuration that was read as <paramref name="fromVersion"/>.
    /// </summary>
    /// <param name="config">The parsed configuration; it is migrated in place.</param>
    /// <param name="fromVersion">The schema version the configuration was read as.</param>
    /// <returns>The migrated and validated configuration plus diagnostics.</returns>
    /// <exception cref="ConfigMigrationException">
    /// When no step covers some version in the chain or a step throws.
    /// </exception>
    public MigrationResult Migrate(PanelConfig config, int fromVersion)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromVersion, CurrentVersion);

        var version = fromVersion;
        var working = config;
        var diagnostics = new List<ConfigDiagnostic>();

        while (version < CurrentVersion)
        {
            if (!_byFromVersion.TryGetValue(version, out var step))
            {
                throw new ConfigMigrationException(
                    $"No migration step exists from schema version {version} to {CurrentVersion}.");
            }

            try
            {
                working = step.Migrate(working)
                    ?? throw new ConfigMigrationException(
                        $"The migration from schema {version} to {step.ToVersion} returned no configuration.");
            }
            catch (ConfigMigrationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ConfigMigrationException(
                    $"The migration from schema {version} to {step.ToVersion} failed ({ex.GetType().Name}).",
                    ex);
            }

            working.SchemaVersion = step.ToVersion;
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.migration.applied",
                $"Migrated the configuration from schema {version} to {step.ToVersion}."));
            version = step.ToVersion;
        }

        working.SchemaVersion = CurrentVersion;

        // RF-076: the migrated result is validated before the store may persist it.
        diagnostics.AddRange(ConfigValidator.Validate(working, _clockCulture));

        return new MigrationResult
        {
            Config = working,
            FromVersion = fromVersion,
            ToVersion = CurrentVersion,
            Diagnostics = diagnostics,
        };
    }
}
