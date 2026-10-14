using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;

namespace TouchPanel.Tests.Config;

/// <summary>
/// JSON round-trips for the M3 widget types (RF-046/M3-RF-468/469): Clock mode/format, ImageButton
/// showLabel, and Bar sensor/range/showValue survive serialize→deserialize, an unknown Clock mode
/// maps to the lenient sentinel, and unknown discriminators stay preserved as placeholders.
/// </summary>
public sealed class ConfigJsonCustomizationTests
{
    [Fact]
    public void ClockWidget_RoundTrips()
    {
        var original = CreatePanel(new ClockWidgetConfig
        {
            Id = "clock",
            X = 4,
            Y = 8,
            Width = 240,
            Height = 120,
            Label = "Time",
            Mode = ClockMode.Time12,
            Format = "HH:mm:ss",
        });

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        var clock = Assert.IsType<ClockWidgetConfig>(restored.Pages[0].Widgets[0]);
        Assert.Equal(ClockMode.Time12, clock.Mode);
        Assert.Equal("HH:mm:ss", clock.Format);
        Assert.Contains("\"time12\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageButtonWidget_RoundTrips()
    {
        var original = CreatePanel(new ImageButtonWidgetConfig
        {
            Id = "ib",
            Width = 120,
            Height = 120,
            Label = "Mute",
            Icon = "mute",
            ShowLabel = false,
            Action = new MuteActionConfig(),
        });

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        var imageButton = Assert.IsType<ImageButtonWidgetConfig>(restored.Pages[0].Widgets[0]);
        Assert.Equal("mute", imageButton.Icon);
        Assert.False(imageButton.ShowLabel);
        Assert.IsType<MuteActionConfig>(imageButton.Action);
        Assert.Contains("\"showLabel\": false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BarWidget_RoundTrips()
    {
        var original = CreatePanel(new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Label = "CPU",
            Sensor = "cpu.total",
            Minimum = 5,
            Maximum = 42,
            ShowValue = false,
        });

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        var bar = Assert.IsType<BarWidgetConfig>(restored.Pages[0].Widgets[0]);
        Assert.Equal("cpu.total", bar.Sensor);
        Assert.Equal(5, bar.Minimum);
        Assert.Equal(42, bar.Maximum);
        Assert.False(bar.ShowValue);
    }

    [Fact]
    public void ImageWidget_RoundTrips()
    {
        var original = CreatePanel(new ImageWidgetConfig
        {
            Id = "image",
            X = 4,
            Y = 8,
            Width = 240,
            Height = 160,
            Label = "Logo",
            Path = @"C:\Pictures\logo.png",
            Stretch = ImageStretchMode.UniformToFill,
        });

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        var image = Assert.IsType<ImageWidgetConfig>(restored.Pages[0].Widgets[0]);
        Assert.Equal(@"C:\Pictures\logo.png", image.Path);
        Assert.Equal(ImageStretchMode.UniformToFill, image.Stretch);
        Assert.Contains("\"type\": \"image\"", json, StringComparison.Ordinal);
        Assert.Contains("\"uniformToFill\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageWidget_UnknownStretch_MapsToTheLenientSentinel()
    {
        var json = """
            { "type": "image", "id": "img", "path": "C:\\Pictures\\logo.png", "stretch": "sideways" }
            """;

        var widget = JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options)!;

        Assert.Equal(ImageStretchMode.Unknown, Assert.IsType<ImageWidgetConfig>(widget).Stretch);
    }

    [Fact]
    public void ClockWidget_UnknownMode_MapsToTheLenientSentinel()
    {
        var json = """
            { "type": "clock", "id": "c", "mode": "sideways" }
            """;

        var widget = JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options)!;

        Assert.Equal(ClockMode.Unknown, Assert.IsType<ClockWidgetConfig>(widget).Mode);
    }

    [Fact]
    public void UnknownWidgetDiscriminator_IsPreservedAsAPlaceholder()
    {
        // M3-RF-469b: a same-schema build that merely lacks the type preserves it with its raw JSON.
        var json = """
            { "type": "heatmap", "id": "h", "x": 5, "series": [1, 2, 3] }
            """;

        var widget = Assert.IsType<UnknownWidgetConfig>(
            JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options)!);

        Assert.Equal("heatmap", widget.Type);
        Assert.Equal(3, widget.Raw.GetProperty("series").GetArrayLength());
    }

    // ---- User-requested addition: optional byte/data-rate display unit (no schema bump) ----

    [Fact]
    public void GaugeWidget_DisplayUnit_RoundTrips()
    {
        var original = CreatePanel(new GaugeWidgetConfig
        {
            Id = "g",
            Width = 200,
            Height = 200,
            Sensor = "net.down",
            Minimum = 0,
            Maximum = 100,
            DisplayUnit = DisplayUnit.Megabytes,
        });

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Contains("\"displayUnit\": \"megabytes\"", json, StringComparison.Ordinal);
        Assert.Equal(DisplayUnit.Megabytes, Assert.IsType<GaugeWidgetConfig>(restored.Pages[0].Widgets[0]).DisplayUnit);
    }

    [Fact]
    public void BarWidget_DisplayUnit_RoundTrips()
    {
        var original = CreatePanel(new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Sensor = "net.up",
            DisplayUnit = DisplayUnit.Gigabytes,
        });

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Equal(DisplayUnit.Gigabytes, Assert.IsType<BarWidgetConfig>(restored.Pages[0].Widgets[0]).DisplayUnit);
    }

    [Fact]
    public void WidgetWithoutDisplayUnit_LoadsAsAuto()
    {
        // An older/hand-written file that predates the field must load unchanged: the missing
        // optional field defaults to auto.
        var gaugeJson = """{ "type": "gauge", "id": "g", "sensor": "cpu.total" }""";
        var barJson = """{ "type": "bar", "id": "b", "sensor": "cpu.total" }""";

        Assert.Equal(
            DisplayUnit.Auto,
            Assert.IsType<GaugeWidgetConfig>(JsonSerializer.Deserialize<WidgetConfig>(gaugeJson, ConfigJson.Options)!).DisplayUnit);
        Assert.Equal(
            DisplayUnit.Auto,
            Assert.IsType<BarWidgetConfig>(JsonSerializer.Deserialize<WidgetConfig>(barJson, ConfigJson.Options)!).DisplayUnit);
    }

    [Fact]
    public void WidgetWithUnknownDisplayUnit_MapsToTheLenientSentinel()
    {
        var json = """{ "type": "gauge", "id": "g", "sensor": "cpu.total", "displayUnit": "furlongs" }""";

        var widget = JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options)!;

        Assert.Equal(DisplayUnit.Unknown, Assert.IsType<GaugeWidgetConfig>(widget).DisplayUnit);
    }

    // ---- User-requested addition: Sparkline current-value readout (no schema bump) ----

    [Fact]
    public void SparklineWidget_ShowValueAndDisplayUnit_RoundTrip()
    {
        var original = CreatePanel(new SparklineWidgetConfig
        {
            Id = "sp",
            Width = 240,
            Height = 100,
            Sensor = "net.down",
            WindowSeconds = 120,
            ShowValue = true,
            DisplayUnit = DisplayUnit.Kilobytes,
        });

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Contains("\"showValue\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"displayUnit\": \"kilobytes\"", json, StringComparison.Ordinal);

        var sparkline = Assert.IsType<SparklineWidgetConfig>(restored.Pages[0].Widgets[0]);
        Assert.True(sparkline.ShowValue);
        Assert.Equal(DisplayUnit.Kilobytes, sparkline.DisplayUnit);
    }

    [Fact]
    public void SparklineWidget_MissingReadoutFields_DefaultToGraphOnlyAndAuto()
    {
        // An older/hand-written file that predates the readout must load unchanged: the missing
        // optional fields default to ShowValue=false and DisplayUnit=Auto.
        var json = """{ "type": "sparkline", "id": "sp", "sensor": "cpu.total" }""";

        var sparkline = Assert.IsType<SparklineWidgetConfig>(
            JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options)!);

        Assert.False(sparkline.ShowValue);
        Assert.Equal(DisplayUnit.Auto, sparkline.DisplayUnit);
    }

    private static PanelConfig CreatePanel(WidgetConfig widget) =>
        new()
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig { Id = "main", Widgets = [widget] },
            ],
        };
}
