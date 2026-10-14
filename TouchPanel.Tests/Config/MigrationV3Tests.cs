using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Migrations;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Schema v3 migration fixtures (T-14, RF-075/RF-076, M2-RF-435/M2-RF-436): the appearance override
/// fields are additive over v2 and default to "theme default" (null), a v2 file still loads through
/// the ordered chain to the current schema, and the v2→v3 step is deterministic and idempotent.
/// </summary>
public sealed class MigrationV3Tests
{
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

    [Fact]
    public void V2ToV3Migration_IsAdditiveAndIdempotent()
    {
        var config = JsonSerializer.Deserialize<PanelConfig>(V2Json, ConfigJson.Options)!;
        var migration = new V2ToV3Migration();

        var migrated = migration.Migrate(config);

        Assert.Equal(3, migrated.SchemaVersion);
        Assert.Equal(2, migration.FromVersion);
        Assert.Equal(3, migration.ToVersion);

        // A v2 widget has no appearance fields; they stay unset ("theme default").
        var button = Assert.IsType<ButtonWidgetConfig>(migrated.Pages[0].Widgets[0]);
        Assert.Null(button.Icon);
        Assert.Null(button.Foreground);
        Assert.Null(button.Background);
        Assert.Null(button.Accent);
        Assert.Null(button.FontSize);
        Assert.Null(button.Opacity);

        // Running the step again over its own output changes nothing observable.
        var again = migration.Migrate(migrated);
        Assert.Equal(3, again.SchemaVersion);
        Assert.Equal("Go", again.Pages[0].Widgets[0].Label);
    }

    [Fact]
    public void FullChain_MigratesV2ToCurrentSchema()
    {
        var config = JsonSerializer.Deserialize<PanelConfig>(V2Json, ConfigJson.Options)!;
        var runner = new MigrationRunner(ConfigMigrationChain.Create());

        Assert.True(runner.CanMigrateFrom(2));
        var result = runner.Migrate(config, fromVersion: 2);

        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.Config.SchemaVersion);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.applied");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void V2File_WithAppearanceFieldsAlreadyPresent_PreservesThem()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "pages": [
                {
                  "id": "main",
                  "widgets": [
                    { "type": "label", "id": "l1", "x": 0, "y": 0, "width": 100, "height": 40,
                      "label": "Hi", "icon": "power", "foreground": "#AABBCC", "fontSize": 18, "opacity": 0.4 }
                  ]
                }
              ]
            }
            """;

        var config = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var result = new MigrationRunner(ConfigMigrationChain.Create()).Migrate(config, fromVersion: 2);

        var label = Assert.IsType<LabelWidgetConfig>(result.Config.Pages[0].Widgets[0]);
        Assert.Equal("power", label.Icon);
        Assert.Equal("#AABBCC", label.Foreground);
        Assert.Equal(18, label.FontSize);
        Assert.Equal(0.4, label.Opacity);
    }
}
