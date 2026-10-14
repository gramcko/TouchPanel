using TouchPanel.Config;
using TouchPanel.Config.Migrations;
using TouchPanel.Config.Validation;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

public class MigrationTests
{
    [Fact]
    public void Migrate_AppliesOrderedStepsAndValidatesTheResult()
    {
        var runner = new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]);
        var config = CreateVersionedConfig(schemaVersion: 0, x: 10);

        var result = runner.Migrate(config, fromVersion: 0);

        Assert.Equal(0, result.FromVersion);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.ToVersion);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.Config.SchemaVersion);
        Assert.Equal(10 + SyntheticV0ToV1Migration.MarkerOffset, result.Config.Pages[0].Widgets[0].X);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.applied");
    }

    [Fact]
    public void Migrate_ValidatesTheMigratedResult_BeyondPageBoundsFindingIsReported()
    {
        var runner = new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]);
        var config = CreateVersionedConfig(
            schemaVersion: 0,
            x: ConfigValidator.MaxWidgetExtent - 1); // + marker offset pushes the widget out of bounds

        var result = runner.Migrate(config, fromVersion: 0);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.widget.geometry");
        Assert.Equal(ConfigValidator.MaxWidgetExtent - 100, result.Config.Pages[0].Widgets[0].X);
    }

    [Fact]
    public void Migrate_IsIdempotent_ForTheSameInputAndSourceVersion()
    {
        var runner = new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]);

        var first = runner.Migrate(CreateVersionedConfig(schemaVersion: 0, x: 10), fromVersion: 0);
        var second = runner.Migrate(CreateVersionedConfig(schemaVersion: 0, x: 10), fromVersion: 0);

        Assert.Equal(first.Config.Pages[0].Widgets[0].X, second.Config.Pages[0].Widgets[0].X);
        Assert.Equal(first.Config.SchemaVersion, second.Config.SchemaVersion);
    }

    [Fact]
    public void Migrate_FromCurrentVersion_PerformsNoSteps()
    {
        var runner = new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]);
        var config = CreateVersionedConfig(PanelConfig.CurrentSchemaVersion, x: 10);

        var result = runner.Migrate(config, fromVersion: PanelConfig.CurrentSchemaVersion);

        Assert.Equal(10, result.Config.Pages[0].Widgets[0].X);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.applied");
    }

    [Fact]
    public void Migrate_WithoutAStepForTheSourceVersion_Throws()
    {
        var runner = new MigrationRunner();
        var config = CreateVersionedConfig(schemaVersion: 0, x: 0);

        var exception = Assert.Throws<ConfigMigrationException>(() => runner.Migrate(config, fromVersion: 0));

        Assert.Contains("No migration step", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrate_WhenAStepThrows_WrapsTheFailure()
    {
        var runner = new MigrationRunner([new SyntheticV0ToV1Migration { Fail = true }]);

        var exception = Assert.Throws<ConfigMigrationException>(
            () => runner.Migrate(CreateVersionedConfig(0, 0), fromVersion: 0));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void Migrate_AppliesMultipleStepsInVersionOrder()
    {
        var order = new List<int>();
        var runner = new MigrationRunner(
            [
                new RecordingMigration(1, 2, order),
                new RecordingMigration(0, 1, order),
            ],
            currentVersion: 2);

        var result = runner.Migrate(CreateVersionedConfig(0, 0), fromVersion: 0);

        Assert.Equal(new[] { 0, 1 }, order);
        Assert.Equal(2, result.Config.SchemaVersion);
        Assert.Equal(new[] { 0, 1 }, runner.Migrations.Select(migration => migration.FromVersion));
    }

    [Fact]
    public void CanMigrateFrom_ReportsChainAvailability()
    {
        var runner = new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]);

        Assert.True(runner.CanMigrateFrom(0));
        Assert.True(runner.CanMigrateFrom(PanelConfig.CurrentSchemaVersion));
        Assert.False(runner.CanMigrateFrom(-1));
        Assert.False(runner.CanMigrateFrom(PanelConfig.CurrentSchemaVersion + 1));
    }

    [Fact]
    public void Constructor_RejectsBackwardsOrDuplicateOrFutureSteps()
    {
        Assert.Throws<ArgumentException>(
            () => new MigrationRunner([new RecordingMigration(1, 1, [])]));
        Assert.Throws<ArgumentException>(
            () => new MigrationRunner([new RecordingMigration(2, 1, [])]));
        Assert.Throws<ArgumentException>(
            () => new MigrationRunner([new RecordingMigration(0, PanelConfig.CurrentSchemaVersion + 1, [])]));
        Assert.Throws<ArgumentException>(
            () => new MigrationRunner(
                [new RecordingMigration(0, 1, []), new RecordingMigration(0, 2, [])],
                currentVersion: 2));
    }

    private static PanelConfig CreateVersionedConfig(int schemaVersion, double x) =>
        new()
        {
            SchemaVersion = schemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets =
                    [
                        new LabelWidgetConfig { Id = "w", X = x, Y = 10, Width = 100, Height = 50 },
                    ],
                },
            ],
        };

    private sealed class RecordingMigration(int fromVersion, int toVersion, List<int> order) : IConfigMigration
    {
        public int FromVersion { get; } = fromVersion;

        public int ToVersion { get; } = toVersion;

        public PanelConfig Migrate(PanelConfig config)
        {
            order.Add(FromVersion);
            return config;
        }
    }
}
