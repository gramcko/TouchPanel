using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;

namespace TouchPanel.Tests.Config;

public class DefaultConfigGeneratorTests
{
    // Minimum supported panel: 1024×600 at 100% (RF-023).
    private const double MonitorWidth = 1024;
    private const double MonitorHeight = 600;
    private const double MinimumTarget = 40; // D-9/RF-031

    [Fact]
    public void Create_UsesCurrentSchemaVersion()
    {
        var config = DefaultConfigGenerator.Create();

        Assert.Equal(PanelConfig.CurrentSchemaVersion, config.SchemaVersion);
    }

    [Fact]
    public void Create_EmitsSchemaV4AppearanceDefaults()
    {
        var config = DefaultConfigGenerator.Create();

        Assert.Equal(4, config.SchemaVersion);
        Assert.Equal("dark", config.Appearance.Theme);
        Assert.Null(config.Appearance.AccentColor);
        Assert.Equal("default", config.Appearance.IconPack);
    }

    [Fact]
    public void Create_HasSingleActivePageWhoseIdIsValid()
    {
        var config = DefaultConfigGenerator.Create();

        var page = Assert.Single(config.Pages);
        Assert.Equal(DefaultConfigGenerator.DefaultPageId, page.Id);
        Assert.Equal(page.Id, config.ActivePageId);
    }

    [Fact]
    public void Create_ContainsLabelButtonsToggleAndGauge()
    {
        var widgets = DefaultConfigGenerator.Create().Pages[0].Widgets;

        Assert.Contains(widgets, widget => widget is LabelWidgetConfig);
        Assert.True(widgets.Count(widget => widget is ButtonWidgetConfig) >= 2, "expected a few buttons");
        Assert.Contains(widgets, widget => widget is ToggleWidgetConfig);
        Assert.Contains(widgets, widget => widget is GaugeWidgetConfig);
    }

    [Fact]
    public void Create_EveryWidgetFits1024x600AndMeetsMinimumTouchTarget()
    {
        var config = DefaultConfigGenerator.Create();

        foreach (var page in config.Pages)
        {
            foreach (var widget in page.Widgets)
            {
                Assert.True(double.IsFinite(widget.X), $"{widget.Id} X is not finite");
                Assert.True(double.IsFinite(widget.Y), $"{widget.Id} Y is not finite");
                Assert.True(double.IsFinite(widget.Width), $"{widget.Id} Width is not finite");
                Assert.True(double.IsFinite(widget.Height), $"{widget.Id} Height is not finite");

                Assert.True(widget.X >= 0, $"{widget.Id} starts left of the page");
                Assert.True(widget.Y >= 0, $"{widget.Id} starts above the page");
                Assert.True(widget.Width >= MinimumTarget, $"{widget.Id} is narrower than {MinimumTarget} DIP");
                Assert.True(widget.Height >= MinimumTarget, $"{widget.Id} is shorter than {MinimumTarget} DIP");
                Assert.True(widget.X + widget.Width <= MonitorWidth, $"{widget.Id} exceeds {MonitorWidth} DIP width");
                Assert.True(widget.Y + widget.Height <= MonitorHeight, $"{widget.Id} exceeds {MonitorHeight} DIP height");
            }
        }
    }

    [Fact]
    public void Create_WidgetIdsAreUniqueWithinEachPage()
    {
        var config = DefaultConfigGenerator.Create();

        foreach (var page in config.Pages)
        {
            var ids = page.Widgets.Select(widget => widget.Id).ToArray();
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(ids, string.IsNullOrEmpty);
        }
    }

    [Fact]
    public void Create_ActionWidgetsBindKnownActions()
    {
        var widgets = DefaultConfigGenerator.Create().Pages[0].Widgets;

        foreach (var widget in widgets.OfType<ButtonWidgetConfig>())
        {
            Assert.NotNull(widget.Action);
            Assert.IsNotType<UnknownActionConfig>(widget.Action);
        }

        var toggle = Assert.Single(widgets.OfType<ToggleWidgetConfig>());
        Assert.NotNull(toggle.Action);
        Assert.IsNotType<UnknownActionConfig>(toggle.Action);
        Assert.False(toggle.InitialOn);
    }

    [Fact]
    public void Create_GaugeHasSensorAndDisplayRange()
    {
        var gauge = Assert.Single(DefaultConfigGenerator.Create().Pages[0].Widgets.OfType<GaugeWidgetConfig>());

        Assert.False(string.IsNullOrWhiteSpace(gauge.Sensor));
        Assert.True(gauge.Maximum > gauge.Minimum);
    }

    [Fact]
    public void Create_UsesPrimaryFallbackForFirstRun()
    {
        var config = DefaultConfigGenerator.Create();

        Assert.Null(config.Monitor.Match);
        Assert.Equal(MonitorFallback.Primary, config.Monitor.Fallback);
        Assert.False(config.Window.AlwaysOnTop);
        Assert.False(config.Startup.StartWithWindows);
    }

    [Fact]
    public void Create_ReturnsFreshInstancePerCall()
    {
        var first = DefaultConfigGenerator.Create();
        var second = DefaultConfigGenerator.Create();

        first.Pages[0].Widgets.Clear();
        first.Window.AlwaysOnTop = true;

        Assert.NotEmpty(second.Pages[0].Widgets);
        Assert.False(second.Window.AlwaysOnTop);
    }

    [Fact]
    public void Create_RoundTripsWithoutUnknownEntries()
    {
        var original = DefaultConfigGenerator.Create();

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var result = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Equal(original.ActivePageId, result.ActivePageId);
        Assert.Empty(FindUnknownWidgets(result));
        Assert.All(
            result.Pages[0].Widgets.Where(widget => widget.Action is not null),
            widget => Assert.IsNotType<UnknownActionConfig>(widget.Action));

        Assert.Equal(
            original.Pages[0].Widgets.Select(widget => widget.Type),
            result.Pages[0].Widgets.Select(widget => widget.Type));
    }

    private static IEnumerable<UnknownWidgetConfig> FindUnknownWidgets(PanelConfig config) =>
        config.Pages.SelectMany(page => page.Widgets).OfType<UnknownWidgetConfig>();
}
