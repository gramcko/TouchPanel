using System.Text.Json;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Hotkey-reference action shape, JSON round-trip, and validation (M5-RF-520..524): the additive
/// <c>hotkey</c> discriminator serializes/deserializes under schema v4, and a widget referencing a
/// missing/blank hotkey is disabled with a reason and never dropped while a valid reference stays
/// enabled.
/// </summary>
public sealed class HotkeyActionConfigTests
{
    private const string PageId = "main";

    [Fact]
    public void HotkeyAction_RoundTripsInSchemaV4()
    {
        var config = ConfigWith(
            new ButtonWidgetConfig { Id = "b1", Action = new RunHotkeyActionConfig { HotkeyId = "h1" } },
            new HotkeyConfig { Id = "h1", Modifiers = ["Ctrl"], Key = "A", Action = new MuteActionConfig() });

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);

        Assert.Contains("\"type\": \"hotkey\"", json);
        Assert.Contains("\"hotkeyId\": \"h1\"", json);

        var roundTripped = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var action = Assert.IsType<RunHotkeyActionConfig>(roundTripped.Pages[0].Widgets[0].Action);
        Assert.Equal("h1", action.HotkeyId);
        Assert.Equal(4, roundTripped.SchemaVersion);
        Assert.Equal(4, PanelConfig.CurrentSchemaVersion);
    }

    [Fact]
    public void HotkeyAction_DirectDeserialization_SelectsTheConcreteSubtype()
    {
        var action = JsonSerializer.Deserialize<ActionConfig>(
            """{ "type": "hotkey", "hotkeyId": "h1" }""",
            ConfigJson.Options);

        var hotkey = Assert.IsType<RunHotkeyActionConfig>(action);
        Assert.Equal("h1", hotkey.HotkeyId);
        Assert.Equal(ActionTypes.Hotkey, hotkey.Type);
    }

    [Fact]
    public void ValidHotkeyReference_StaysEnabled()
    {
        var widget = new ButtonWidgetConfig { Id = "b1", Action = new RunHotkeyActionConfig { HotkeyId = "h1" } };
        var config = ConfigWith(widget, new HotkeyConfig { Id = "h1", Modifiers = ["Ctrl"], Key = "A", Action = new MuteActionConfig() });

        var diagnostics = ConfigValidator.Validate(config);

        Assert.True(widget.IsEnabled);
        Assert.Null(widget.DisabledReason);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.hotkey.reference");
    }

    [Fact]
    public void MissingHotkeyReference_DisablesTheWidgetWithAReasonAndKeepsIt()
    {
        var widget = new ButtonWidgetConfig { Id = "b1", Action = new RunHotkeyActionConfig { HotkeyId = "missing" } };
        var config = ConfigWith(widget);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.False(widget.IsEnabled);
        Assert.NotNull(widget.DisabledReason);
        Assert.Contains("missing", widget.DisabledReason!, StringComparison.Ordinal);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.hotkey.reference");
        Assert.Single(config.Pages[0].Widgets);
    }

    [Fact]
    public void BlankHotkeyReference_DisablesTheWidgetWithAReason()
    {
        var widget = new ButtonWidgetConfig { Id = "b1", Action = new RunHotkeyActionConfig { HotkeyId = "   " } };
        var config = ConfigWith(widget, new HotkeyConfig { Id = "h1", Modifiers = ["Ctrl"], Key = "A", Action = new MuteActionConfig() });

        ConfigValidator.Validate(config);

        Assert.False(widget.IsEnabled);
        Assert.NotNull(widget.DisabledReason);
    }

    [Fact]
    public void DuplicateHotkeyIds_ResolveFirstWinsSoTheReferenceIsValid()
    {
        var widget = new ButtonWidgetConfig { Id = "b1", Action = new RunHotkeyActionConfig { HotkeyId = "dup" } };
        var config = ConfigWith(
            widget,
            new HotkeyConfig { Id = "dup", Modifiers = ["Ctrl"], Key = "A", Action = new MuteActionConfig() },
            new HotkeyConfig { Id = "dup", Modifiers = ["Alt"], Key = "B", Action = new MediaKeyActionConfig() });

        ConfigValidator.Validate(config);

        Assert.True(widget.IsEnabled);
    }

    [Fact]
    public void ValidateWidgetAction_CrossChecksTheHotkeyReference()
    {
        var widget = new ButtonWidgetConfig { Id = "b1", Action = new RunHotkeyActionConfig { HotkeyId = "h1" } };

        var missing = ConfigValidator.ValidateWidgetAction(widget, []);
        Assert.False(widget.IsEnabled);
        Assert.Contains(missing, diagnostic => diagnostic.Code == "config.widget.action.hotkey.reference");

        widget.IsEnabled = true;
        widget.DisabledReason = null;
        ConfigValidator.ValidateWidgetAction(widget, [new HotkeyConfig { Id = "h1", Action = new MuteActionConfig() }]);
        Assert.True(widget.IsEnabled);
    }

    private static PanelConfig ConfigWith(WidgetConfig widget, params HotkeyConfig[] hotkeys) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = PageId,
        Hotkeys = [.. hotkeys],
        Pages = [new PageConfig { Id = PageId, Widgets = [widget] }],
    };
}
