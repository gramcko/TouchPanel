using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Validation;

namespace TouchPanel.Tests.Config;

public class ConfigValidatorTests
{
    [Fact]
    public void DefaultConfig_PassesValidationWithoutWarnings()
    {
        var config = DefaultConfigGenerator.Create();

        var diagnostics = ConfigValidator.Validate(config);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
        Assert.All(config.Pages[0].Widgets, widget => Assert.True(widget.IsEnabled));
    }

    // --- RF-079 entry tier: unknown types are disabled, never dropped -------------------------

    [Fact]
    public void UnknownWidgetType_IsKeptButDisabledWithDiagnostic()
    {
        var raw = JsonDocument.Parse("""{ "type": "heatmap", "id": "w9", "x": 5, "y": 5 }""").RootElement.Clone();
        var unknown = new UnknownWidgetConfig("heatmap", raw);
        var ok = new LabelWidgetConfig { Id = "ok", Width = 100, Height = 50 };
        var config = CreateConfig(unknown, ok);

        var diagnostics = ConfigValidator.Validate(config);

        var widgets = config.Pages[0].Widgets;
        Assert.Equal(2, widgets.Count);
        Assert.Same(unknown, widgets[0]);
        Assert.False(unknown.IsEnabled);
        Assert.NotNull(unknown.DisabledReason);
        Assert.True(ok.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.unknownType");
    }

    [Fact]
    public void UnknownActionType_DisablesWidgetAndPreservesAction()
    {
        var action = new UnknownActionConfig(
            "http",
            JsonDocument.Parse("""{ "type": "http", "url": "https://example.test" }""").RootElement.Clone());
        var button = new ButtonWidgetConfig { Id = "b1", Width = 100, Height = 50, Action = action };
        var config = CreateConfig(button);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.False(button.IsEnabled);
        Assert.Same(action, button.Action);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.unknown");
    }

    [Fact]
    public void ButtonAndToggleWithoutAction_AreDisabledButLabelStaysEnabled()
    {
        var button = new ButtonWidgetConfig { Id = "b", Width = 100, Height = 50 };
        var toggle = new ToggleWidgetConfig { Id = "t", Width = 100, Height = 50 };
        var label = new LabelWidgetConfig { Id = "l", Width = 100, Height = 50 };
        var config = CreateConfig(button, toggle, label);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.False(button.IsEnabled);
        Assert.False(toggle.IsEnabled);
        Assert.True(label.IsEnabled);
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Code == "config.widget.action.missing"));
    }

    [Fact]
    public void RunCommandWithoutExecutablePath_DisablesWidget()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "run",
            Width = 100,
            Height = 50,
            Action = new RunCommandActionConfig { Path = "   " },
        };
        var config = CreateConfig(button);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.False(button.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.invalid");
    }

    [Fact]
    public void RunCommandWithNullArgumentEntries_RemovesThemWithoutDisabling()
    {
        var action = new RunCommandActionConfig
        {
            Path = @"C:\tools\app.exe",
            Arguments = ["ok", null!, string.Empty],
            WorkingDirectory = "   ",
        };
        var button = new ButtonWidgetConfig { Id = "run", Width = 100, Height = 50, Action = action };

        ConfigValidator.Validate(CreateConfig(button));

        Assert.True(button.IsEnabled);
        Assert.Equal(["ok", string.Empty], action.Arguments);
        Assert.Null(action.WorkingDirectory);
    }

    [Fact]
    public void MediaKeyWithUnknownSentinel_DisablesWidget()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "media",
            Width = 100,
            Height = 50,
            Action = new MediaKeyActionConfig { Key = MediaKeyKind.Unknown },
        };

        ConfigValidator.Validate(CreateConfig(button));

        Assert.False(button.IsEnabled);
    }

    [Fact]
    public void MediaKeyWithKnownKey_StaysEnabled()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "media",
            Width = 100,
            Height = 50,
            Action = new MediaKeyActionConfig { Key = MediaKeyKind.Stop },
        };

        ConfigValidator.Validate(CreateConfig(button));

        Assert.True(button.IsEnabled);
    }

    [Fact]
    public void VolumeWithUnknownMode_DisablesWidget()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "vol",
            Width = 100,
            Height = 50,
            Action = new VolumeActionConfig { Mode = VolumeMode.Unknown },
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(button));

        Assert.False(button.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.invalid");
    }

    [Fact]
    public void VolumeStepWithUnknownDirection_DisablesWidget()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "vol",
            Width = 100,
            Height = 50,
            Action = new VolumeActionConfig { Mode = VolumeMode.Step, Direction = VolumeDirection.Unknown },
        };

        ConfigValidator.Validate(CreateConfig(button));

        Assert.False(button.IsEnabled);
    }

    [Theory]
    [InlineData(250, 100)]
    [InlineData(-25, 0)]
    public void VolumeSetLevel_IsClampedWithoutDisabling(int level, int expected)
    {
        var action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = level };
        var button = new ButtonWidgetConfig { Id = "vol", Width = 100, Height = 50, Action = action };

        ConfigValidator.Validate(CreateConfig(button));

        Assert.True(button.IsEnabled);
        Assert.Equal(expected, action.Level);
    }

    // --- Gauge rules (RF-034, RF-079) ---------------------------------------------------------

    [Fact]
    public void GaugeWithoutSensor_IsDisabled()
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g",
            Width = 100,
            Height = 100,
            Sensor = " ",
            Minimum = 0,
            Maximum = 100,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(gauge));

        Assert.False(gauge.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.gauge.sensor");
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(50, 50)]
    [InlineData(double.NaN, 100)]
    [InlineData(0, double.PositiveInfinity)]
    public void GaugeWithInvalidRange_IsResetToDefaults(double minimum, double maximum)
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g",
            Width = 100,
            Height = 100,
            Sensor = "cpu.total",
            Minimum = minimum,
            Maximum = maximum,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(gauge));

        Assert.True(gauge.IsEnabled);
        Assert.Equal(0, gauge.Minimum);
        Assert.Equal(100, gauge.Maximum);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.gauge.range");
    }

    // --- Geometry and ids (RF-079) ------------------------------------------------------------

    [Fact]
    public void Geometry_NegativeAndOversizedValues_AreClampedIntoPageBounds()
    {
        var widget = new LabelWidgetConfig
        {
            Id = "w",
            X = -20,
            Y = double.NaN,
            Width = 0,
            Height = 999_999,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(widget));

        Assert.Equal(0, widget.X);
        Assert.Equal(0, widget.Y);
        Assert.Equal(ConfigValidator.MinWidgetSize, widget.Width);
        Assert.Equal(ConfigValidator.MaxWidgetExtent, widget.Height);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.geometry");
    }

    [Fact]
    public void Geometry_PositionBeyondMaxExtent_IsClampedSoTheWidgetStillFits()
    {
        var widget = new LabelWidgetConfig
        {
            Id = "w",
            X = 1e308,
            Y = 1e308,
            Width = 100,
            Height = 100,
        };

        ConfigValidator.Validate(CreateConfig(widget));

        Assert.Equal(ConfigValidator.MaxWidgetExtent - 100, widget.X);
        Assert.Equal(ConfigValidator.MaxWidgetExtent - 100, widget.Y);
    }

    [Fact]
    public void MissingWidgetId_IsSynthesizedDeterministicallyAndAvoidsCollisions()
    {
        var unnamed = new LabelWidgetConfig { Width = 50, Height = 50 };
        var named = new LabelWidgetConfig { Id = "widget-1", Width = 50, Height = 50 };

        ConfigValidator.Validate(CreateConfig(unnamed, named));

        Assert.Equal("widget-1-1", unnamed.Id);
        Assert.Equal("widget-1", named.Id);
    }

    [Fact]
    public void DuplicateWidgetIdsOnAPage_AreFlaggedWithoutDisabling()
    {
        var first = new LabelWidgetConfig { Id = "dup", Width = 50, Height = 50 };
        var second = new LabelWidgetConfig { Id = "dup", Width = 50, Height = 50 };

        var diagnostics = ConfigValidator.Validate(CreateConfig(first, second));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.duplicateId");
        Assert.True(first.IsEnabled);
        Assert.True(second.IsEnabled);
    }

    [Fact]
    public void UniqueWidgetIds_DoNotProduceADuplicateDiagnostic()
    {
        var first = new LabelWidgetConfig { Id = "a", Width = 50, Height = 50 };
        var second = new LabelWidgetConfig { Id = "b", Width = 50, Height = 50 };

        var diagnostics = ConfigValidator.Validate(CreateConfig(first, second));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.duplicateId");
    }

    // --- Pages and active-page resolution (RF-037, RF-079) ------------------------------------

    [Fact]
    public void ActivePageId_Valid_IsKept()
    {
        var config = CreateConfig(new LabelWidgetConfig { Id = "w", Width = 50, Height = 50 });
        config.Pages.Add(new PageConfig { Id = "second" });
        config.ActivePageId = "second";

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("second", config.ActivePageId);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.activePage.substituted");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing")]
    public void ActivePageId_MissingOrInvalid_FallsBackToFirstPageWithDiagnostic(string? activePageId)
    {
        var config = CreateConfig(new LabelWidgetConfig { Id = "w", Width = 50, Height = 50 });
        config.Pages.Add(new PageConfig { Id = "second" });
        config.ActivePageId = activePageId;

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("main", config.ActivePageId);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.activePage.substituted");
    }

    [Fact]
    public void EmptyPages_GainGeneratedDefaultPageAndResolveActivePage()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "missing",
            Pages = [],
        };

        var diagnostics = ConfigValidator.Validate(config);

        var page = Assert.Single(config.Pages);
        Assert.Equal(DefaultConfigGenerator.DefaultPageId, page.Id);
        Assert.Equal(page.Id, config.ActivePageId);
        Assert.NotEmpty(page.Widgets);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.pages.empty");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.activePage.substituted");
    }

    [Fact]
    public void DuplicatePageIds_ResolveToTheFirstPageDeterministically()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "dup",
            Pages =
            [
                new PageConfig { Id = "dup", Widgets = [new LabelWidgetConfig { Id = "first", Width = 50, Height = 50 }] },
                new PageConfig { Id = "dup", Widgets = [new LabelWidgetConfig { Id = "second", Width = 50, Height = 50 }] },
            ],
        };

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("dup", config.ActivePageId);
        Assert.Equal("first", config.Pages[0].Widgets[0].Id);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.page.duplicateId");
    }

    [Fact]
    public void MissingPageId_IsSynthesized()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = null,
            Pages = [new PageConfig()],
        };

        ConfigValidator.Validate(config);

        Assert.False(string.IsNullOrWhiteSpace(config.Pages[0].Id));
        Assert.Equal(config.Pages[0].Id, config.ActivePageId);
    }

    // --- Root-level rules ---------------------------------------------------------------------

    [Fact]
    public void UnknownMonitorFallback_Sentinel_IsReplacedWithNone()
    {
        var config = DefaultConfigGenerator.Create();
        config.Monitor.Fallback = MonitorFallback.Unknown;

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal(MonitorFallback.None, config.Monitor.Fallback);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.monitor.fallback");
    }

    [Fact]
    public void BlankMonitorIdentityFields_AreIgnored()
    {
        var config = DefaultConfigGenerator.Create();
        config.Monitor.Match = new MonitorIdentity
        {
            HardwareId = "   ",
            DevicePath = string.Empty,
            FriendlyName = "Touch",
        };

        ConfigValidator.Validate(config);

        Assert.Null(config.Monitor.Match.HardwareId);
        Assert.Null(config.Monitor.Match.DevicePath);
        Assert.Equal("Touch", config.Monitor.Match.FriendlyName);
    }

    // --- Schema v2: telemetry, slider, sparkline, send-keys (T-01) ---------------------------

    [Theory]
    [InlineData(0.1, TelemetryConfig.MinPollIntervalSeconds)]
    [InlineData(10.0, TelemetryConfig.MaxPollIntervalSeconds)]
    [InlineData(double.NaN, TelemetryConfig.DefaultPollIntervalSeconds)]
    [InlineData(0.0, TelemetryConfig.MinPollIntervalSeconds)]
    public void TelemetryInterval_OutOfRange_IsClampedWithWarning(double interval, double expected)
    {
        var config = DefaultConfigGenerator.Create();
        config.Telemetry.PollIntervalSeconds = interval;

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal(expected, config.Telemetry.PollIntervalSeconds);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.telemetry.interval");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(5.0)]
    public void TelemetryInterval_InRange_IsKeptWithoutDiagnostic(double interval)
    {
        var config = DefaultConfigGenerator.Create();
        config.Telemetry.PollIntervalSeconds = interval;

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal(interval, config.Telemetry.PollIntervalSeconds);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.telemetry.interval");
    }

    [Fact]
    public void SliderWithInvalidRangeAndStep_IsResetToDefaults()
    {
        var slider = new SliderWidgetConfig
        {
            Id = "s",
            Width = 200,
            Height = 60,
            Minimum = 100,
            Maximum = 0,
            Step = 0,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(slider));

        Assert.Equal(0, slider.Minimum);
        Assert.Equal(100, slider.Maximum);
        Assert.Equal(1, slider.Step);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.slider.range");
    }

    [Fact]
    public void SliderValue_IsClampedAndSnapped()
    {
        var slider = new SliderWidgetConfig
        {
            Id = "s",
            Width = 200,
            Height = 60,
            Minimum = 0,
            Maximum = 10,
            Step = 2,
            Value = 7,
        };

        ConfigValidator.Validate(CreateConfig(slider));

        Assert.Equal(8, slider.Value);
    }

    [Fact]
    public void SliderWithUnknownOrientation_FallsBackToHorizontal()
    {
        var slider = new SliderWidgetConfig
        {
            Id = "s",
            Width = 200,
            Height = 60,
            Minimum = 0,
            Maximum = 100,
            Step = 1,
            Orientation = SliderOrientation.Unknown,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(slider));

        Assert.Equal(SliderOrientation.Horizontal, slider.Orientation);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.slider.orientation");
    }

    [Fact]
    public void SparklineWithoutSensor_IsDisabled()
    {
        var sparkline = new SparklineWidgetConfig { Id = "sp", Width = 200, Height = 100, Sensor = " " };

        var diagnostics = ConfigValidator.Validate(CreateConfig(sparkline));

        Assert.False(sparkline.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.sparkline.sensor");
    }

    [Theory]
    [InlineData(1, SparklineWidgetConfig.MinWindowSeconds)]
    [InlineData(99_999, SparklineWidgetConfig.MaxWindowSeconds)]
    [InlineData(double.NaN, SparklineWidgetConfig.DefaultWindowSeconds)]
    public void SparklineWindow_IsClampedIntoBounds(double window, double expected)
    {
        var sparkline = new SparklineWidgetConfig
        {
            Id = "sp",
            Width = 200,
            Height = 100,
            Sensor = "cpu.total",
            WindowSeconds = window,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(sparkline));

        Assert.Equal(expected, sparkline.WindowSeconds);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.sparkline.window");
    }

    [Fact]
    public void SparklineWithUnknownStyle_FallsBackToLine()
    {
        var sparkline = new SparklineWidgetConfig
        {
            Id = "sp",
            Width = 200,
            Height = 100,
            Sensor = "cpu.total",
            Style = SparklineStyle.Unknown,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(sparkline));

        Assert.Equal(SparklineStyle.Line, sparkline.Style);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.sparkline.style");
    }

    [Fact]
    public void SparklineWithInvalidExplicitScale_IsSetToAutoScale()
    {
        var sparkline = new SparklineWidgetConfig
        {
            Id = "sp",
            Width = 200,
            Height = 100,
            Sensor = "cpu.total",
            Minimum = 100,
            Maximum = 0,
        };

        ConfigValidator.Validate(CreateConfig(sparkline));

        Assert.Null(sparkline.Minimum);
        Assert.Null(sparkline.Maximum);
    }

    [Fact]
    public void SendKeysWithoutKey_DisablesWidget()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "b",
            Width = 100,
            Height = 50,
            Action = new SendKeysActionConfig { Key = "  " },
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(button));

        Assert.False(button.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.sendKeys.key");
    }

    [Fact]
    public void SendKeysWithKeyAndNullModifier_StaysEnabledAndCleansModifiers()
    {
        var action = new SendKeysActionConfig { Key = "A", Modifiers = ["Alt", null!] };
        var button = new ButtonWidgetConfig { Id = "b", Width = 100, Height = 50, Action = action };

        ConfigValidator.Validate(CreateConfig(button));

        Assert.True(button.IsEnabled);
        Assert.Equal(["Alt"], action.Modifiers);
    }

    [Fact]
    public void SendKeysWithUnknownKey_DisablesWidget()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "b",
            Width = 100,
            Height = 50,
            Action = new SendKeysActionConfig { Key = "Foo", Modifiers = ["Alt"] },
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(button));

        Assert.False(button.IsEnabled);
        Assert.NotNull(button.DisabledReason);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.sendKeys.key");
    }

    [Fact]
    public void SendKeysWithUnknownModifier_DisablesWidget()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "b",
            Width = 100,
            Height = 50,
            Action = new SendKeysActionConfig { Key = "A", Modifiers = ["Hyper"] },
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(button));

        Assert.False(button.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.sendKeys.modifier");
    }

    [Fact]
    public void SendKeysWithBareKey_StaysEnabled()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "b",
            Width = 100,
            Height = 50,
            Action = new SendKeysActionConfig { Key = "F5" },
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(button));

        Assert.True(button.IsEnabled);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void TogglePersistState_IsPassedThroughUnchanged()
    {
        var toggle = new ToggleWidgetConfig
        {
            Id = "t",
            Width = 100,
            Height = 50,
            PersistState = true,
            Action = new MuteActionConfig(),
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(toggle));

        Assert.True(toggle.PersistState);
        Assert.True(toggle.IsEnabled);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
    }

    private static PanelConfig CreateConfig(params WidgetConfig[] widgets) =>
        new()
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets = [.. widgets],
                },
            ],
        };
}
