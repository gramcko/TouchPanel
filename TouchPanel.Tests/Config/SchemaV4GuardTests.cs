using System.Reflection;
using System.Text.Json;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Migrations;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Schema and session-only guards for the M4 promotion (M4-RF-441/M4-RF-961/D-M4-3/D-M4-6,
/// AC-M4-13): the schema stays <b>v4</b> with no <c>V4ToV5</c> step, and grid size / snap remain
/// session-only runtime state that is never persisted to <c>panel.json</c>.
/// </summary>
public sealed class SchemaV4GuardTests
{
    [Fact]
    public void CurrentSchemaVersion_StaysV4()
    {
        Assert.Equal(4, PanelConfig.CurrentSchemaVersion);
    }

    [Fact]
    public void MigrationChain_EndsAtV4_WithNoV5Step()
    {
        var chain = ConfigMigrationChain.Create();

        Assert.All(chain, step => Assert.True(step.ToVersion <= 4));
        Assert.Equal(4, chain.Max(step => step.ToVersion));
        Assert.DoesNotContain(chain, step => step.FromVersion >= 4);
    }

    [Fact]
    public void NoV4ToV5MigrationTypeExists()
    {
        var migrationTypes = typeof(PanelConfig).Assembly.GetTypes()
            .Where(type => typeof(IConfigMigration).IsAssignableFrom(type)
                && type is { IsAbstract: false, IsInterface: false })
            .ToArray();

        Assert.DoesNotContain(migrationTypes, type => type.Name.Contains("V5", StringComparison.OrdinalIgnoreCase));
        Assert.All(migrationTypes, type => Assert.DoesNotContain("V4ToV5", type.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConfigNamespace_DeclaresNoGridOrSnapField()
    {
        var offenders = typeof(PanelConfig).Assembly.GetTypes()
            .Where(type => type.Namespace is not null
                && (type.Namespace == "TouchPanel.Config"
                    || type.Namespace.StartsWith("TouchPanel.Config.", StringComparison.Ordinal))
                && type.IsPublic)
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(property => $"{type.Name}.{property.Name}"))
            .Where(name => name.Contains("grid", StringComparison.OrdinalIgnoreCase)
                || name.Contains("snap", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SerializedPanelJson_ContainsNoGridOrSnapField()
    {
        var json = JsonSerializer.Serialize(DefaultConfigGenerator.Create(), ConfigJson.Options);

        Assert.DoesNotContain("grid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("snap", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GridAndSnap_AreSessionOnly_AndDefaultToTheCodeConstant()
    {
        // The default is a code constant, not a schema default.
        Assert.Equal(8, GridSnap.DefaultGridSize);

        var config = DefaultConfigGenerator.Create();
        var service = CreateService(config);
        service.Enter(1024, 600);

        // A fresh session starts snap-on at the default grid.
        Assert.Equal(GridSnap.DefaultGridSize, service.GridSize);
        Assert.True(service.IsSnapEnabled);

        // A value set during a session is honoured until restart...
        service.SetGridSize(16);
        Assert.Equal(16, service.GridSize);

        // ...but is never persisted to the configuration.
        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        Assert.DoesNotContain("grid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("snap", json, StringComparison.OrdinalIgnoreCase);

        // A fresh session (simulated restart) resets to the documented default.
        var fresh = CreateService(config);
        fresh.Enter(1024, 600);
        Assert.Equal(GridSnap.DefaultGridSize, fresh.GridSize);
        Assert.True(fresh.IsSnapEnabled);
    }

    [Fact]
    public void HotkeyAction_RoundTripsUnderSchemaV4_WithNoSchemaBump()
    {
        // M5-RF-524: the additive 'hotkey' discriminator needs no schema bump; a v4 file carrying it
        // round-trips unchanged and the configured version stays 4.
        var config = new PanelConfig
        {
            SchemaVersion = 4,
            ActivePageId = "main",
            Hotkeys = [new HotkeyConfig { Id = "h1", Modifiers = ["Ctrl"], Key = "A", Action = new MuteActionConfig() }],
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets = [new ButtonWidgetConfig { Id = "b1", Action = new RunHotkeyActionConfig { HotkeyId = "h1" } }],
                },
            ],
        };

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        var roundTripped = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Equal(4, roundTripped.SchemaVersion);
        Assert.Equal(4, PanelConfig.CurrentSchemaVersion);
        var action = Assert.IsType<RunHotkeyActionConfig>(roundTripped.Pages[0].Widgets[0].Action);
        Assert.Equal("h1", action.HotkeyId);
    }

    private static EditModeService CreateService(PanelConfig config)
    {
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        return new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
    }
}
