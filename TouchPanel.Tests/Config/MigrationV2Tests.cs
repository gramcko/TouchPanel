using System.IO;
using System.Text.Json;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Migrations;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// T-01 schema v2 migration fixtures (RF-075/RF-076, AC-M2-02): a real v1 file loads, migrates and
/// validates, is saved atomically at schema v2, and has its original preserved as
/// <c>panel.backup.json</c>; a failing migration follows the invalid path and leaves the source
/// intact. Also covers the additive migration no-loss guarantee.
/// </summary>
public class MigrationV2Tests
{
    private const string V1Json = """
        {
          "schemaVersion": 1,
          "activePageId": "main",
          "monitor": { "fallback": "none" },
          "window": { "alwaysOnTop": true },
          "startup": { "startWithWindows": false },
          "pages": [
            {
              "id": "main",
              "widgets": [
                { "type": "label", "id": "title", "x": 16, "y": 16, "width": 300, "height": 44, "label": "TouchPanel" },
                {
                  "type": "toggle",
                  "id": "mute",
                  "x": 16,
                  "y": 80,
                  "width": 200,
                  "height": 120,
                  "initialOn": true,
                  "action": { "type": "mute" }
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task LoadAsync_V1File_MigratesToV2ValidatesAndBacksUpTheOriginal()
    {
        using var temp = new TempDirectory();
        var originalBytes = WritePanel(temp, V1Json);
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.OlderSupported, result.State);
        Assert.Equal(1, result.DetectedSchemaVersion);
        var config = Assert.IsType<PanelConfig>(result.Config);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.applied");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migrated.saved");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error);

        // New v2 sections carry the documented defaults.
        Assert.Equal(TelemetryConfig.DefaultPollIntervalSeconds, config.Telemetry.PollIntervalSeconds);
        Assert.Empty(config.Hotkeys);

        // No v1 data is lost.
        Assert.True(config.Window.AlwaysOnTop);
        var toggle = Assert.Single(
            config.Pages[0].Widgets.OfType<ToggleWidgetConfig>());
        Assert.True(toggle.InitialOn);
        Assert.False(toggle.PersistState);

        // panel.json was atomically replaced with the migrated current-schema configuration.
        var persisted = JsonSerializer.Deserialize<PanelConfig>(
            File.ReadAllText(temp.Config.PanelFile), ConfigJson.Options)!;
        Assert.Equal(PanelConfig.CurrentSchemaVersion, persisted.SchemaVersion);
        Assert.True(persisted.Window.AlwaysOnTop);
        Assert.Equal(TelemetryConfig.DefaultPollIntervalSeconds, persisted.Telemetry.PollIntervalSeconds);

        // The original v1 source is preserved as the backup, byte-for-byte.
        Assert.True(File.Exists(temp.Config.BackupFile));
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.BackupFile));
        var backup = JsonSerializer.Deserialize<PanelConfig>(
            File.ReadAllText(temp.Config.BackupFile), ConfigJson.Options)!;
        Assert.Equal(1, backup.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_FailingMigration_FollowsTheInvalidPathAndLeavesTheSourceIntact()
    {
        using var temp = new TempDirectory();
        var originalBytes = WritePanel(temp, V1Json);
        var (store, _) = CreateStore(temp, new MigrationRunner([new ThrowingV1ToV2Migration()]));

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.Equal(1, result.DetectedSchemaVersion);
        Assert.NotNull(result.Config);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.failed");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "config.migrated.saved");

        // RF-076: the source is never destructively overwritten; it is preserved for diagnosis.
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
        var preserved = Assert.Single(Directory.GetFiles(temp.DirectoryPath, ConfigPaths.CorruptFileSearchPattern));
        Assert.Equal(originalBytes, File.ReadAllBytes(preserved));
        Assert.False(File.Exists(temp.Config.BackupFile));
    }

    [Fact]
    public void V1ToV2Migration_IsAdditiveAndIdempotent()
    {
        var config = JsonSerializer.Deserialize<PanelConfig>(V1Json, ConfigJson.Options)!;
        var migration = new V1ToV2Migration();

        var migrated = migration.Migrate(config);

        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Equal(TelemetryConfig.DefaultPollIntervalSeconds, migrated.Telemetry.PollIntervalSeconds);
        Assert.True(migrated.Window.AlwaysOnTop);
        Assert.Equal(1, migration.FromVersion);
        Assert.Equal(2, migration.ToVersion);

        // Running the step again over its own output changes nothing observable.
        var again = migration.Migrate(migrated);
        Assert.Equal(2, again.SchemaVersion);
        Assert.Equal(TelemetryConfig.DefaultPollIntervalSeconds, again.Telemetry.PollIntervalSeconds);
        Assert.True(again.Window.AlwaysOnTop);
    }

    private static (JsonConfigStore Store, RecordingSink Sink) CreateStore(
        TempDirectory temp,
        MigrationRunner? migrations = null)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new JsonConfigStore(temp.Config, migrations, timeProvider: null, logger), sink);
    }

    private static byte[] WritePanel(TempDirectory temp, string json)
    {
        File.WriteAllText(temp.Config.PanelFile, json);
        return File.ReadAllBytes(temp.Config.PanelFile);
    }

    private sealed class ThrowingV1ToV2Migration : IConfigMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 2;

        public PanelConfig Migrate(PanelConfig config) =>
            throw new InvalidOperationException("synthetic v1→v2 failure");
    }
}
