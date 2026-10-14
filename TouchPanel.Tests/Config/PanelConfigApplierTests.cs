using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Shared apply-pipeline behavior (T-11 part 2, MD-10/M2-RF-824): every step runs in a deterministic
/// order, one failing step does not stop the others, and the tracked-metric helper reflects the
/// gauge/sparkline sensors of the active page (M2-RF-693).
/// </summary>
public sealed class PanelConfigApplierTests
{
    [Fact]
    public void Apply_RunsEveryStepInOrder()
    {
        var target = new RecordingPanelConfigApplyTarget();
        var applier = new PanelConfigApplier(target, logger: null);
        var config = DefaultConfigGenerator.Create();

        var failures = applier.Apply(config);

        Assert.Equal(0, failures);
        Assert.Equal(
            [
                "window-options",
                "monitor-target",
                "appearance",
                "active-page",
                "telemetry-interval",
                "tracked-metrics",
                "hotkeys",
                "toggle-state",
            ],
            target.Steps);
        Assert.Same(config.Window, target.LastWindow);
        Assert.Same(config.Monitor, target.LastMonitor);
        Assert.Same(config.Appearance, target.LastAppearance);
        Assert.Same(config, target.LastRebuilt);
        Assert.Same(config.Telemetry, target.LastTelemetry);
        Assert.Same(config, target.LastTracked);
        Assert.Same(config.Hotkeys, target.LastHotkeys);
        Assert.Same(config, target.LastToggleReconciled);
    }

    [Fact]
    public void Apply_OneStepFails_TheRemainingStepsStillRun()
    {
        var target = new RecordingPanelConfigApplyTarget { FailStep = "active-page" };
        var applier = new PanelConfigApplier(target, logger: null);

        var failures = applier.Apply(DefaultConfigGenerator.Create());

        Assert.Equal(1, failures);
        Assert.DoesNotContain("active-page", target.Steps);
        Assert.Contains("telemetry-interval", target.Steps);
        Assert.Contains("hotkeys", target.Steps);
        Assert.Contains("toggle-state", target.Steps);
    }

    [Fact]
    public void Apply_AppearanceStepFails_TheRemainingStepsStillRun()
    {
        var target = new RecordingPanelConfigApplyTarget { FailStep = "appearance" };
        var applier = new PanelConfigApplier(target, logger: null);

        var failures = applier.Apply(DefaultConfigGenerator.Create());

        Assert.Equal(1, failures);
        Assert.DoesNotContain("appearance", target.Steps);
        Assert.Contains("active-page", target.Steps);
        Assert.Contains("hotkeys", target.Steps);
        Assert.Contains("toggle-state", target.Steps);
    }

    [Fact]
    public void CollectTrackedSensorKeys_UsesTheActivePageAndRemovesDuplicates()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "second",
            Pages =
            [
                new PageConfig
                {
                    Id = "first",
                    Widgets = [new GaugeWidgetConfig { Sensor = "ignored.cpu" }],
                },
                new PageConfig
                {
                    Id = "second",
                    Widgets =
                    [
                        new GaugeWidgetConfig { Sensor = "cpu.total" },
                        new SparklineWidgetConfig { Sensor = "gpu.temp" },
                        new SparklineWidgetConfig { Sensor = "CPU.TOTAL" },
                        new GaugeWidgetConfig { Sensor = "  " },
                    ],
                },
            ],
        };

        var keys = PanelConfigApplier.CollectTrackedSensorKeys(config);

        Assert.Equal(new[] { "cpu.total", "gpu.temp" }, keys);
    }

    [Fact]
    public void CollectTrackedSensorKeys_EmptyPages_ReturnsEmpty()
    {
        var config = new PanelConfig { SchemaVersion = PanelConfig.CurrentSchemaVersion, Pages = [] };

        Assert.Empty(PanelConfigApplier.CollectTrackedSensorKeys(config));
    }

    [Fact]
    public void CollectMaxSparklineWindowSeconds_UsesTheActivePageMaximum()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "second",
            Pages =
            [
                new PageConfig
                {
                    Id = "first",
                    Widgets = [new SparklineWidgetConfig { WindowSeconds = 3600 }],
                },
                new PageConfig
                {
                    Id = "second",
                    Widgets =
                    [
                        new GaugeWidgetConfig { Sensor = "cpu.total" },
                        new SparklineWidgetConfig { WindowSeconds = 60 },
                        new SparklineWidgetConfig { WindowSeconds = 300 },
                    ],
                },
            ],
        };

        Assert.Equal(300, PanelConfigApplier.CollectMaxSparklineWindowSeconds(config));
    }

    [Theory]
    [InlineData(5, SparklineWidgetConfig.MinWindowSeconds)]
    [InlineData(99_999, SparklineWidgetConfig.MaxWindowSeconds)]
    [InlineData(double.NaN, SparklineWidgetConfig.DefaultWindowSeconds)]
    public void CollectMaxSparklineWindowSeconds_NormalizesIntoTheSpecRange(double configured, double expected)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "page",
            Pages =
            [
                new PageConfig
                {
                    Id = "page",
                    Widgets = [new SparklineWidgetConfig { WindowSeconds = configured }],
                },
            ],
        };

        Assert.Equal(expected, PanelConfigApplier.CollectMaxSparklineWindowSeconds(config));
    }

    [Fact]
    public void CollectMaxSparklineWindowSeconds_NoSparklines_KeepsTheDefault()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "page",
            Pages =
            [
                new PageConfig
                {
                    Id = "page",
                    Widgets = [new GaugeWidgetConfig { Sensor = "cpu.total" }],
                },
            ],
        };

        Assert.Equal(SparklineWidgetConfig.DefaultWindowSeconds, PanelConfigApplier.CollectMaxSparklineWindowSeconds(config));

        var empty = new PanelConfig { SchemaVersion = PanelConfig.CurrentSchemaVersion, Pages = [] };
        Assert.Equal(SparklineWidgetConfig.DefaultWindowSeconds, PanelConfigApplier.CollectMaxSparklineWindowSeconds(empty));
    }

    [Fact]
    public void CollectMaxSparklineWindowSeconds_DrivesStoreCapacityForTheSharedInterval()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "page",
            Pages =
            [
                new PageConfig
                {
                    Id = "page",
                    Widgets =
                    [
                        new SparklineWidgetConfig { Sensor = "cpu.total", WindowSeconds = 60 },
                        new SparklineWidgetConfig { Sensor = "gpu.usage", WindowSeconds = 300 },
                    ],
                },
            ],
        };

        // The shared store is sized to the maximum referenced window at the current interval
        // (M2-RF-691); each widget then slices its own window for rendering (M2-RF-415).
        using var store = new SensorHistoryStore(TimeSpan.FromSeconds(1));
        store.Reconfigure(PanelConfigApplier.CollectMaxSparklineWindowSeconds(config), store.Interval);

        Assert.Equal(300, store.WindowSeconds);
        Assert.Equal(300, store.Capacity);
    }
}
