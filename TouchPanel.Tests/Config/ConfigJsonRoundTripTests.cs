using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;

namespace TouchPanel.Tests.Config;

public class ConfigJsonRoundTripTests
{
    [Fact]
    public void PanelConfig_RoundTrips_WithAllSectionsAndWidgetTypes()
    {
        var original = new PanelConfig
        {
            SchemaVersion = 1,
            ActivePageId = "main",
            Monitor = new MonitorConfig
            {
                Match = new MonitorIdentity
                {
                    HardwareId = "HW-ACME-1234",
                    DevicePath = @"\\?\DISPLAY#ACME#1234",
                    FriendlyName = "Touch Screen",
                },
                Fallback = MonitorFallback.Primary,
            },
            Window = new WindowConfig { AlwaysOnTop = true },
            Startup = new StartupConfig { StartWithWindows = true },
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets =
                    [
                        new ButtonWidgetConfig
                        {
                            Id = "b1",
                            X = 1.5,
                            Y = 2.5,
                            Width = 120,
                            Height = 60,
                            Label = "Run",
                            Action = new RunCommandActionConfig
                            {
                                Path = @"C:\Program Files\App\app.exe",
                                Arguments = ["--file", "a b", "quote\"inside", "$(calc)", "; rm -rf /"],
                                WorkingDirectory = @"C:\Temp\Work Dir",
                            },
                        },
                        new ToggleWidgetConfig
                        {
                            Id = "t1",
                            X = 130,
                            Y = 2.5,
                            Width = 120,
                            Height = 60,
                            Label = "Next",
                            InitialOn = true,
                            Action = new MediaKeyActionConfig { Key = MediaKeyKind.Next },
                        },
                        new LabelWidgetConfig
                        {
                            Id = "l1",
                            X = 0,
                            Y = 100,
                            Width = 200,
                            Height = 40,
                            Label = "Hello",
                        },
                        new GaugeWidgetConfig
                        {
                            Id = "g1",
                            X = 0,
                            Y = 150,
                            Width = 200,
                            Height = 200,
                            Label = "CPU",
                            Sensor = "cpu.total",
                            Minimum = 10,
                            Maximum = 90,
                        },
                    ],
                },
                new PageConfig { Id = "empty" },
            ],
        };

        var result = RoundTrip(original);

        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal("main", result.ActivePageId);
        Assert.NotNull(result.Monitor.Match);
        Assert.Equal("HW-ACME-1234", result.Monitor.Match.HardwareId);
        Assert.Equal(@"\\?\DISPLAY#ACME#1234", result.Monitor.Match.DevicePath);
        Assert.Equal("Touch Screen", result.Monitor.Match.FriendlyName);
        Assert.Equal(MonitorFallback.Primary, result.Monitor.Fallback);
        Assert.True(result.Window.AlwaysOnTop);
        Assert.True(result.Startup.StartWithWindows);
        Assert.Equal(2, result.Pages.Count);
        Assert.Empty(result.Pages[1].Widgets);

        var widgets = result.Pages[0].Widgets;
        Assert.Collection(
            widgets,
            widget =>
            {
                var button = Assert.IsType<ButtonWidgetConfig>(widget);
                Assert.Equal("b1", button.Id);
                Assert.Equal(1.5, button.X);
                Assert.Equal(2.5, button.Y);
                Assert.Equal(120, button.Width);
                Assert.Equal(60, button.Height);
                Assert.Equal("Run", button.Label);
                var run = Assert.IsType<RunCommandActionConfig>(button.Action);
                Assert.Equal(@"C:\Program Files\App\app.exe", run.Path);
                Assert.Equal(
                    new[] { "--file", "a b", "quote\"inside", "$(calc)", "; rm -rf /" },
                    run.Arguments);
                Assert.Equal(@"C:\Temp\Work Dir", run.WorkingDirectory);
            },
            widget =>
            {
                var toggle = Assert.IsType<ToggleWidgetConfig>(widget);
                Assert.True(toggle.InitialOn);
                var media = Assert.IsType<MediaKeyActionConfig>(toggle.Action);
                Assert.Equal(MediaKeyKind.Next, media.Key);
            },
            widget =>
            {
                var label = Assert.IsType<LabelWidgetConfig>(widget);
                Assert.Equal("Hello", label.Label);
                Assert.Null(label.Action);
            },
            widget =>
            {
                var gauge = Assert.IsType<GaugeWidgetConfig>(widget);
                Assert.Equal("cpu.total", gauge.Sensor);
                Assert.Equal(10, gauge.Minimum);
                Assert.Equal(90, gauge.Maximum);
            });
    }

    [Fact]
    public void RunCommandAction_RoundTrips_ThroughBaseType()
    {
        var original = new RunCommandActionConfig
        {
            Path = @"C:\tools\my app.exe",
            Arguments = ["--flag", "value with spaces", "a\"b", "$(whoami)", "& echo hi"],
            WorkingDirectory = @"C:\work\dir",
        };

        var result = Assert.IsType<RunCommandActionConfig>(RoundTrip<ActionConfig>(original));

        Assert.Equal(ActionTypes.RunCommand, result.Type);
        Assert.Equal(original.Path, result.Path);
        Assert.Equal(original.Arguments, result.Arguments);
        Assert.Equal(original.WorkingDirectory, result.WorkingDirectory);
    }

    [Fact]
    public void MediaKeyAction_RoundTrips_ThroughBaseType()
    {
        var result = Assert.IsType<MediaKeyActionConfig>(
            RoundTrip<ActionConfig>(new MediaKeyActionConfig { Key = MediaKeyKind.Previous }));

        Assert.Equal(ActionTypes.MediaKey, result.Type);
        Assert.Equal(MediaKeyKind.Previous, result.Key);
    }

    [Theory]
    [InlineData(VolumeMode.Set, 30, VolumeDirection.Up)]
    [InlineData(VolumeMode.Step, 50, VolumeDirection.Down)]
    public void VolumeAction_RoundTrips_ThroughBaseType(VolumeMode mode, int level, VolumeDirection direction)
    {
        var result = Assert.IsType<VolumeActionConfig>(
            RoundTrip<ActionConfig>(new VolumeActionConfig
            {
                Mode = mode,
                Level = level,
                Direction = direction,
            }));

        Assert.Equal(ActionTypes.Volume, result.Type);
        Assert.Equal(mode, result.Mode);
        Assert.Equal(level, result.Level);
        Assert.Equal(direction, result.Direction);
    }

    [Fact]
    public void MuteAction_RoundTrips_ThroughBaseType()
    {
        var result = Assert.IsType<MuteActionConfig>(RoundTrip<ActionConfig>(new MuteActionConfig()));

        Assert.Equal(ActionTypes.Mute, result.Type);
    }

    [Fact]
    public void ButtonWidget_RoundTrips_ThroughBaseType()
    {
        var result = Assert.IsType<ButtonWidgetConfig>(RoundTrip<WidgetConfig>(new ButtonWidgetConfig
        {
            Id = "b1",
            X = 10,
            Y = 20,
            Width = 120,
            Height = 60,
            Label = "Go",
        }));

        Assert.Equal(WidgetTypes.Button, result.Type);
        Assert.Equal("b1", result.Id);
        Assert.Equal("Go", result.Label);
    }

    [Fact]
    public void ToggleWidget_RoundTrips_InitialOn()
    {
        var result = Assert.IsType<ToggleWidgetConfig>(RoundTrip<WidgetConfig>(new ToggleWidgetConfig
        {
            Id = "t1",
            InitialOn = true,
        }));

        Assert.Equal(WidgetTypes.Toggle, result.Type);
        Assert.True(result.InitialOn);
    }

    [Fact]
    public void LabelWidget_RoundTrips()
    {
        var result = Assert.IsType<LabelWidgetConfig>(RoundTrip<WidgetConfig>(new LabelWidgetConfig
        {
            Id = "l1",
            Label = "Status",
        }));

        Assert.Equal(WidgetTypes.Label, result.Type);
        Assert.Equal("Status", result.Label);
    }

    [Fact]
    public void GaugeWidget_RoundTrips()
    {
        var result = Assert.IsType<GaugeWidgetConfig>(RoundTrip<WidgetConfig>(new GaugeWidgetConfig
        {
            Id = "g1",
            Sensor = "gpu.usage",
            Minimum = -5,
            Maximum = 105,
        }));

        Assert.Equal(WidgetTypes.Gauge, result.Type);
        Assert.Equal("gpu.usage", result.Sensor);
        Assert.Equal(-5, result.Minimum);
        Assert.Equal(105, result.Maximum);
    }

    [Fact]
    public void Serialize_UsesCamelCaseNamesAndTypeDiscriminators()
    {
        var json = JsonSerializer.Serialize(DefaultConfigGenerator.Create(), ConfigJson.Options);

        Assert.Contains("\"schemaVersion\": 4", json);
        Assert.Contains("\"activePageId\": \"main\"", json);
        Assert.Contains("\"alwaysOnTop\": false", json);
        Assert.Contains("\"startWithWindows\": false", json);
        Assert.Contains("\"fallback\": \"primary\"", json);
        Assert.Contains("\"type\": \"label\"", json);
        Assert.Contains("\"type\": \"button\"", json);
        Assert.Contains("\"type\": \"toggle\"", json);
        Assert.Contains("\"type\": \"gauge\"", json);
        Assert.Contains("\"type\": \"mediaKey\"", json);
        Assert.Contains("\"type\": \"mute\"", json);
        Assert.Contains("\"mode\": \"step\"", json);
        Assert.Contains("\"type\": \"volume\"", json);
    }

    [Fact]
    public void Serialize_OmitsNullProperties()
    {
        var json = JsonSerializer.Serialize(DefaultConfigGenerator.Create(), ConfigJson.Options);

        Assert.DoesNotContain("\"match\"", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void Deserialize_MinimalDocument_AppliesDefaults()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "pages": [ { "id": "main" } ]
            }
            """;

        var config = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Equal(1, config.SchemaVersion);
        Assert.Null(config.ActivePageId);
        Assert.Null(config.Monitor.Match);
        Assert.Equal(MonitorFallback.None, config.Monitor.Fallback);
        Assert.False(config.Window.AlwaysOnTop);
        Assert.False(config.Startup.StartWithWindows);
        Assert.Single(config.Pages);
        Assert.Empty(config.Pages[0].Widgets);
    }

    [Fact]
    public void Deserialize_ExplicitNullSectionsAndCollections_NormalizesToEmptyInstances()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "monitor": null,
              "window": null,
              "startup": null,
              "pages": [ { "id": "main", "widgets": null } ]
            }
            """;

        var config = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.NotNull(config.Monitor);
        Assert.NotNull(config.Window);
        Assert.NotNull(config.Startup);
        Assert.NotNull(config.Pages);
        Assert.NotNull(config.Pages[0].Widgets);
    }

    [Fact]
    public void SendKeysAction_RoundTrips_ThroughBaseType()
    {
        var result = Assert.IsType<SendKeysActionConfig>(
            RoundTrip<ActionConfig>(new SendKeysActionConfig
            {
                Key = "A",
                Modifiers = ["Alt"],
            }));

        Assert.Equal(ActionTypes.SendKeys, result.Type);
        Assert.Equal("A", result.Key);
        Assert.Equal(["Alt"], result.Modifiers);
    }

    [Fact]
    public void SendKeysAction_BareKey_RoundTripsWithEmptyModifiers()
    {
        var result = Assert.IsType<SendKeysActionConfig>(
            RoundTrip<ActionConfig>(new SendKeysActionConfig { Key = "F5" }));

        Assert.Equal("F5", result.Key);
        Assert.Empty(result.Modifiers);
    }

    [Theory]
    [InlineData(SliderOrientation.Horizontal, 0, 100, 5, 25)]
    [InlineData(SliderOrientation.Vertical, -50, 50, 10, 0)]
    public void SliderWidget_RoundTrips(
        SliderOrientation orientation,
        double minimum,
        double maximum,
        double step,
        double value)
    {
        var result = Assert.IsType<SliderWidgetConfig>(RoundTrip<WidgetConfig>(new SliderWidgetConfig
        {
            Id = "s1",
            X = 10,
            Y = 20,
            Width = 300,
            Height = 60,
            Label = "Volume",
            Minimum = minimum,
            Maximum = maximum,
            Step = step,
            Orientation = orientation,
            Value = value,
        }));

        Assert.Equal(WidgetTypes.Slider, result.Type);
        Assert.Equal(minimum, result.Minimum);
        Assert.Equal(maximum, result.Maximum);
        Assert.Equal(step, result.Step);
        Assert.Equal(orientation, result.Orientation);
        Assert.Equal(value, result.Value);
    }

    [Fact]
    public void SliderWidget_UnknownOrientation_MapsToSentinelOnRead()
    {
        const string json = """
            { "type": "slider", "id": "s1", "orientation": "sideways" }
            """;

        var result = Assert.IsType<SliderWidgetConfig>(DeserializeWidget(json));

        Assert.Equal(SliderOrientation.Unknown, result.Orientation);
    }

    [Fact]
    public void SparklineWidget_RoundTrips_WithAutoScale()
    {
        var result = Assert.IsType<SparklineWidgetConfig>(RoundTrip<WidgetConfig>(new SparklineWidgetConfig
        {
            Id = "sp1",
            X = 10,
            Y = 20,
            Width = 300,
            Height = 120,
            Sensor = "cpu.total",
            WindowSeconds = 120,
            Style = SparklineStyle.Filled,
        }));

        Assert.Equal(WidgetTypes.Sparkline, result.Type);
        Assert.Equal("cpu.total", result.Sensor);
        Assert.Equal(120, result.WindowSeconds);
        Assert.Equal(SparklineStyle.Filled, result.Style);
        Assert.Null(result.Minimum);
        Assert.Null(result.Maximum);
    }

    [Fact]
    public void SparklineWidget_RoundTrips_WithExplicitScale()
    {
        var result = Assert.IsType<SparklineWidgetConfig>(RoundTrip<WidgetConfig>(new SparklineWidgetConfig
        {
            Id = "sp1",
            Sensor = "gpu.usage",
            Minimum = 0,
            Maximum = 100,
        }));

        Assert.Equal(0, result.Minimum);
        Assert.Equal(100, result.Maximum);
    }

    [Fact]
    public void TelemetryAndHotkeys_RoundTrip()
    {
        var original = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Telemetry = new TelemetryConfig { PollIntervalSeconds = 2.5 },
            Hotkeys =
            [
                new HotkeyConfig
                {
                    Id = "hk1",
                    Modifiers = ["Ctrl", "Shift"],
                    Key = "A",
                    Action = new SendKeysActionConfig { Key = "A", Modifiers = ["Ctrl", "Shift"] },
                },
            ],
            Pages = [new PageConfig { Id = "main" }],
        };

        var result = RoundTrip(original);

        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.SchemaVersion);
        Assert.Equal(2.5, result.Telemetry.PollIntervalSeconds);
        var hotkey = Assert.Single(result.Hotkeys);
        Assert.Equal("hk1", hotkey.Id);
        Assert.Equal(["Ctrl", "Shift"], hotkey.Modifiers);
        Assert.Equal("A", hotkey.Key);
        var action = Assert.IsType<SendKeysActionConfig>(hotkey.Action);
        Assert.Equal("A", action.Key);
    }

    [Fact]
    public void ToggleWidget_RoundTrips_PersistState()
    {
        var result = Assert.IsType<ToggleWidgetConfig>(RoundTrip<WidgetConfig>(new ToggleWidgetConfig
        {
            Id = "t1",
            InitialOn = true,
            PersistState = true,
        }));

        Assert.True(result.InitialOn);
        Assert.True(result.PersistState);
    }

    private static WidgetConfig DeserializeWidget(string json) =>
        JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options)!;

    private static T RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, ConfigJson.Options);
        return JsonSerializer.Deserialize<T>(json, ConfigJson.Options)!;
    }
}
