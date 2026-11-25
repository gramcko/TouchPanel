using System.Text.Json;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;

namespace TouchPanel.Tests.Config;

/// <summary>
/// User-requested toggle on-state icon: the optional <c>onIcon</c> field is additive (older files
/// load unchanged, no schema bump), round-trips through JSON, and validation preserves known glyph
/// and vector ids while clearing an unknown id with a warning.
/// </summary>
public sealed class ToggleOnIconConfigTests
{
    [Fact]
    public void OnIcon_DefaultsToNullAndDoesNotChangeTheSchemaVersion()
    {
        var toggle = new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
        };

        Assert.Null(toggle.OnIcon);
        Assert.Equal(4, PanelConfig.CurrentSchemaVersion);
    }

    [Fact]
    public void OnIcon_RoundTripsThroughJsonAndKeepsSchemaVersionFour()
    {
        var config = CreateConfig(new ToggleWidgetConfig
        {
            Id = "toggle-1",
            X = 0,
            Y = 0,
            Width = 100,
            Height = 50,
            Label = "Mic",
            Icon = "microphone",
            OnIcon = "microphoneMuted",
            Action = new MuteActionConfig(),
        });

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);

        Assert.Contains("\"onIcon\": \"microphoneMuted\"", json);
        Assert.Contains("\"schemaVersion\": 4", json);

        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var toggle = Assert.IsType<ToggleWidgetConfig>(restored.Pages[0].Widgets[0]);

        Assert.Equal(4, restored.SchemaVersion);
        Assert.Equal("microphoneMuted", toggle.OnIcon);
    }

    [Fact]
    public void OlderJsonWithoutOnIcon_LoadsWithNullOnIcon()
    {
        const string json = """
            {
              "schemaVersion": 4,
              "activePageId": "main",
              "pages": [
                {
                  "id": "main",
                  "widgets": [
                    {
                      "type": "toggle",
                      "id": "toggle-1",
                      "x": 0,
                      "y": 0,
                      "width": 100,
                      "height": 50,
                      "icon": "power"
                    }
                  ]
                }
              ]
            }
            """;

        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var toggle = Assert.IsType<ToggleWidgetConfig>(restored.Pages[0].Widgets[0]);

        Assert.Equal(4, restored.SchemaVersion);
        Assert.Null(toggle.OnIcon);
        Assert.Equal("power", toggle.Icon);
    }

    [Theory]
    [InlineData("star")]
    [InlineData("microphoneMuted")]
    [InlineData("steam")]
    public void Validation_PreservesKnownGlyphAndVectorOnIcons(string onIcon)
    {
        var toggle = new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
            Icon = "power",
            OnIcon = onIcon,
            Action = new MuteActionConfig(),
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(toggle));

        Assert.Equal(onIcon, toggle.OnIcon);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.onIcon");
    }

    [Fact]
    public void Validation_ClearsAnUnknownOnIconWithAWarningAndKeepsNullNull()
    {
        var toggle = new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
            Icon = "power",
            OnIcon = "not-a-real-icon",
            Action = new MuteActionConfig(),
        };

        var unknownDiagnostics = ConfigValidator.Validate(CreateConfig(toggle));

        Assert.Null(toggle.OnIcon);
        Assert.Contains(unknownDiagnostics, diagnostic => diagnostic.Code == "config.widget.onIcon");

        // An unset on icon stays unset, with no diagnostic.
        var unset = new ToggleWidgetConfig
        {
            Id = "toggle-2",
            Width = 100,
            Height = 50,
            Action = new MuteActionConfig(),
        };

        var unsetDiagnostics = ConfigValidator.Validate(CreateConfig(unset));

        Assert.Null(unset.OnIcon);
        Assert.DoesNotContain(unsetDiagnostics, diagnostic => diagnostic.Code == "config.widget.onIcon");
    }

    [Fact]
    public void OnIconColor_DefaultsToNullAndRoundTripsThroughJson()
    {
        var config = CreateConfig(new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
            Icon = "power",
            OnIcon = "star",
            OnIconColor = "#AABBCC",
            Action = new MuteActionConfig(),
        });

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);

        Assert.Contains("\"onIconColor\": \"#AABBCC\"", json);
        Assert.Contains("\"schemaVersion\": 4", json);

        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var toggle = Assert.IsType<ToggleWidgetConfig>(restored.Pages[0].Widgets[0]);

        Assert.Equal(4, restored.SchemaVersion);
        Assert.Equal("#AABBCC", toggle.OnIconColor);
    }

    [Fact]
    public void OnIconColor_IsNullWhenAbsentFromOlderJson()
    {
        const string json = """
            {
              "schemaVersion": 4,
              "activePageId": "main",
              "pages": [
                {
                  "id": "main",
                  "widgets": [
                    {
                      "type": "toggle",
                      "id": "toggle-1",
                      "x": 0,
                      "y": 0,
                      "width": 100,
                      "height": 50,
                      "icon": "power",
                      "onIcon": "star"
                    }
                  ]
                }
              ]
            }
            """;

        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var toggle = Assert.IsType<ToggleWidgetConfig>(restored.Pages[0].Widgets[0]);

        Assert.Null(toggle.OnIconColor);
    }

    [Theory]
    [InlineData("#aabbcc", "#AABBCC")]
    [InlineData("#80112233", "#80112233")]
    public void Validation_NormalizesAValidOnIconColor(string configured, string expected)
    {
        var toggle = new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
            Icon = "power",
            OnIcon = "star",
            OnIconColor = configured,
            Action = new MuteActionConfig(),
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(toggle));

        Assert.Equal(expected, toggle.OnIconColor);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.appearance.onIconColor");
    }

    [Fact]
    public void Validation_ClearsAnInvalidOnIconColorWithAWarning()
    {
        var toggle = new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
            Icon = "power",
            OnIcon = "star",
            OnIconColor = "not-a-colour",
            Action = new MuteActionConfig(),
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(toggle));

        Assert.Null(toggle.OnIconColor);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.appearance.onIconColor");
    }

    private static PanelConfig CreateConfig(params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Pages = [new PageConfig { Id = "main", Widgets = [.. widgets] }],
    };
}
