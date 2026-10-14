using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Migrations;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Schema v4 migration fixtures (T-08, M3-RF-841/M3-RF-846, RF-075/RF-076): the global
/// <c>appearance</c> section is additive over v3 and defaults to the documented values, a v1/v2/v3
/// file still loads through the ordered chain to the current schema, and the v3→v4 step is
/// deterministic and idempotent.
/// </summary>
public sealed class MigrationV4Tests
{
    private const string V3Json = """
        {
          "schemaVersion": 3,
          "activePageId": "main",
          "monitor": { "fallback": "primary" },
          "window": { "alwaysOnTop": true },
          "startup": { "startWithWindows": true },
          "telemetry": { "pollIntervalSeconds": 2.0 },
          "hotkeys": [
            { "id": "hk1", "modifiers": ["Ctrl"], "key": "A", "action": { "type": "mute" } }
          ],
          "pages": [
            {
              "id": "main",
              "widgets": [
                { "type": "label", "id": "l1", "x": 0, "y": 0, "width": 100, "height": 40,
                  "label": "Hi", "icon": "power", "foreground": "#AABBCC" }
              ]
            }
          ]
        }
        """;

    private const string V2Json = """
        {
          "schemaVersion": 2,
          "activePageId": "main",
          "telemetry": { "pollIntervalSeconds": 1.0 },
          "pages": [
            {
              "id": "main",
              "widgets": [
                { "type": "button", "id": "b1", "x": 0, "y": 0, "width": 100, "height": 50,
                  "label": "Go", "action": { "type": "mute" } }
              ]
            }
          ]
        }
        """;

    private const string V1Json = """
        {
          "schemaVersion": 1,
          "activePageId": "main",
          "window": { "alwaysOnTop": true },
          "pages": [
            {
              "id": "main",
              "widgets": [
                { "type": "label", "id": "title", "x": 16, "y": 16, "width": 300, "height": 44, "label": "TouchPanel" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void V3ToV4Migration_IsAdditiveAndIdempotent()
    {
        var config = JsonSerializer.Deserialize<PanelConfig>(V3Json, ConfigJson.Options)!;
        var migration = new V3ToV4Migration();

        var migrated = migration.Migrate(config);

        Assert.Equal(4, migrated.SchemaVersion);
        Assert.Equal(3, migration.FromVersion);
        Assert.Equal(4, migration.ToVersion);

        // The new section carries the documented defaults.
        Assert.Equal("dark", migrated.Appearance.Theme);
        Assert.Null(migrated.Appearance.AccentColor);
        Assert.Equal("default", migrated.Appearance.IconPack);

        // No v3 data is lost or reshaped.
        Assert.True(migrated.Window.AlwaysOnTop);
        Assert.True(migrated.Startup.StartWithWindows);
        Assert.Equal(2.0, migrated.Telemetry.PollIntervalSeconds);
        Assert.Equal("A", Assert.Single(migrated.Hotkeys).Key);
        var label = Assert.IsType<LabelWidgetConfig>(migrated.Pages[0].Widgets[0]);
        Assert.Equal("power", label.Icon);
        Assert.Equal("#AABBCC", label.Foreground);

        // Running the step again over its own output changes nothing observable.
        var again = migration.Migrate(migrated);
        Assert.Equal(4, again.SchemaVersion);
        Assert.Equal("Hi", again.Pages[0].Widgets[0].Label);
    }

    [Fact]
    public void FullChain_MigratesV3ToCurrentSchemaWithAppearanceDefaults()
    {
        var config = JsonSerializer.Deserialize<PanelConfig>(V3Json, ConfigJson.Options)!;
        var runner = new MigrationRunner(ConfigMigrationChain.Create());

        Assert.True(runner.CanMigrateFrom(3));
        var result = runner.Migrate(config, fromVersion: 3);

        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.Config.SchemaVersion);
        Assert.Equal("dark", result.Config.Appearance.Theme);
        Assert.Equal("default", result.Config.Appearance.IconPack);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.applied");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FullChain_MigratesEveryOlderSupportedVersionToCurrentSchema(int fromVersion)
    {
        var json = fromVersion switch
        {
            1 => V1Json,
            2 => V2Json,
            _ => V3Json,
        };
        var config = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var runner = new MigrationRunner(ConfigMigrationChain.Create());

        Assert.True(runner.CanMigrateFrom(fromVersion));
        var result = runner.Migrate(config, fromVersion: fromVersion);

        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.Config.SchemaVersion);
        Assert.Equal("dark", result.Config.Appearance.Theme);
        Assert.Null(result.Config.Appearance.AccentColor);
        Assert.Equal("default", result.Config.Appearance.IconPack);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void V3File_WithAnAppearanceSectionAlreadyPresent_PreservesIt()
    {
        const string json = """
            {
              "schemaVersion": 3,
              "pages": [
                {
                  "id": "main",
                  "widgets": [
                    { "type": "label", "id": "l1", "x": 0, "y": 0, "width": 100, "height": 40, "label": "Hi" }
                  ]
                }
              ],
              "appearance": { "theme": "light", "accentColor": "#112233", "iconPack": "filled" }
            }
            """;

        var config = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var result = new MigrationRunner(ConfigMigrationChain.Create()).Migrate(config, fromVersion: 3);

        Assert.Equal("light", result.Config.Appearance.Theme);
        Assert.Equal("#112233", result.Config.Appearance.AccentColor);
        Assert.Equal("filled", result.Config.Appearance.IconPack);
    }

    [Fact]
    public void V2ToV3Migration_StillEndsAtV3_SoTheChainComposesInOrder()
    {
        var config = JsonSerializer.Deserialize<PanelConfig>(V2Json, ConfigJson.Options)!;

        var v3 = new V2ToV3Migration().Migrate(config);

        Assert.Equal(3, v3.SchemaVersion);
        Assert.Equal(4, new V3ToV4Migration().Migrate(v3).SchemaVersion);
    }
}
