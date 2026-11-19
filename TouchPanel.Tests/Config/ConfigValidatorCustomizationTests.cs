using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Two-tier validation for the M3 widget types (RF-046/M3-RF-469): Clock mode/format normalization,
/// Bar range/sensor handling, and ImageButton disabled-when-invalid-action — all entry-level
/// (never dropping the widget).
/// </summary>
public sealed class ConfigValidatorCustomizationTests
{
    [Fact]
    public void Clock_UnknownMode_FallsBackToTime24WithAWarning()
    {
        var clock = new ClockWidgetConfig { Id = "c", Width = 200, Height = 100, Mode = ClockMode.Unknown };

        var diagnostics = ConfigValidator.Validate(CreateConfig(clock));

        Assert.Equal(ClockMode.Time24, clock.Mode);
        Assert.True(clock.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.clock.mode");
    }

    [Fact]
    public void Clock_ValidCustomFormat_IsPreserved()
    {
        var clock = new ClockWidgetConfig { Id = "c", Width = 200, Height = 100, Format = "HH:mm:ss" };

        var diagnostics = ConfigValidator.Validate(CreateConfig(clock));

        Assert.Equal("HH:mm:ss", clock.Format);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.clock.format");
    }

    [Theory]
    [InlineData("%")]
    [InlineData("HH\\")]
    public void Clock_InvalidCustomFormat_IsIgnoredWithAWarning(string format)
    {
        var clock = new ClockWidgetConfig { Id = "c", Width = 200, Height = 100, Format = format };

        var diagnostics = ConfigValidator.Validate(CreateConfig(clock));

        Assert.Null(clock.Format);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.clock.format");
    }

    [Fact]
    public void Clock_OverLengthCustomFormat_IsIgnoredWithAWarning()
    {
        var clock = new ClockWidgetConfig
        {
            Id = "c",
            Width = 200,
            Height = 100,
            Format = new string('H', ClockWidgetConfig.MaxFormatLength + 1),
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(clock));

        Assert.Null(clock.Format);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.clock.format");
    }

    [Fact]
    public void Clock_DisplayOnly_StaysEnabledWithoutAnAction()
    {
        var clock = new ClockWidgetConfig { Id = "c", Width = 200, Height = 100 };

        var diagnostics = ConfigValidator.Validate(CreateConfig(clock));

        Assert.True(clock.IsEnabled);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.missing");
    }

    [Fact]
    public void Bar_InvalidRange_IsResetToDefaultsWithAWarning()
    {
        var bar = new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Sensor = SensorKeys.CpuTotal,
            Minimum = 100,
            Maximum = 10,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(bar));

        Assert.Equal(BarWidgetConfig.DefaultMinimum, bar.Minimum);
        Assert.Equal(BarWidgetConfig.DefaultMaximum, bar.Maximum);
        Assert.True(bar.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.bar.range");
    }

    [Fact]
    public void Bar_UnknownSensor_IsDisabledWithAReason()
    {
        var bar = new BarWidgetConfig { Id = "bar", Width = 240, Height = 64, Sensor = "not.a.sensor" };

        var diagnostics = ConfigValidator.Validate(CreateConfig(bar));

        Assert.False(bar.IsEnabled);
        Assert.NotNull(bar.DisabledReason);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.bar.sensor");
    }

    [Fact]
    public void Bar_EmptySensor_IsDisabled()
    {
        var bar = new BarWidgetConfig { Id = "bar", Width = 240, Height = 64, Sensor = "   " };

        var diagnostics = ConfigValidator.Validate(CreateConfig(bar));

        Assert.False(bar.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.bar.sensor");
    }

    [Fact]
    public void Bar_ValidConfig_StaysEnabledWithoutWarnings()
    {
        var bar = new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Sensor = SensorKeys.CpuTotal,
            Minimum = 0,
            Maximum = 100,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(bar));

        Assert.True(bar.IsEnabled);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void Image_WithoutPath_IsDisabledWithAReason()
    {
        var image = new ImageWidgetConfig { Id = "img", Width = 240, Height = 160, Path = "   " };

        var diagnostics = ConfigValidator.Validate(CreateConfig(image));

        Assert.False(image.IsEnabled);
        Assert.NotNull(image.DisabledReason);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.image.path");
    }

    [Fact]
    public void Image_RelativePath_IsDisabled()
    {
        var image = new ImageWidgetConfig { Id = "img", Width = 240, Height = 160, Path = @"Pictures\logo.png" };

        var diagnostics = ConfigValidator.Validate(CreateConfig(image));

        Assert.False(image.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.image.path");
    }

    [Fact]
    public void Image_AbsolutePath_StaysEnabledWithoutWarnings()
    {
        var image = new ImageWidgetConfig
        {
            Id = "img",
            Width = 240,
            Height = 160,
            Path = @"C:\Pictures\logo.png",
            Stretch = ImageStretchMode.Fill,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(image));

        Assert.True(image.IsEnabled);
        Assert.Equal(ImageStretchMode.Fill, image.Stretch);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void Image_UnknownStretch_FallsBackToUniformWithAWarning()
    {
        var image = new ImageWidgetConfig
        {
            Id = "img",
            Width = 240,
            Height = 160,
            Path = @"C:\Pictures\logo.png",
            Stretch = ImageStretchMode.Unknown,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(image));

        Assert.Equal(ImageStretchMode.Uniform, image.Stretch);
        Assert.True(image.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.image.stretch");
    }

    [Fact]
    public void ImageButton_WithoutAction_IsDisabledButKept()
    {
        var imageButton = new ImageButtonWidgetConfig { Id = "ib", Width = 120, Height = 120 };

        var diagnostics = ConfigValidator.Validate(CreateConfig(imageButton));

        Assert.False(imageButton.IsEnabled);
        Assert.NotNull(imageButton.DisabledReason);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.missing");
    }

    [Fact]
    public void ImageButton_WithValidAction_StaysEnabled()
    {
        var imageButton = new ImageButtonWidgetConfig
        {
            Id = "ib",
            Width = 120,
            Height = 120,
            Action = new MuteActionConfig(),
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(imageButton));

        Assert.True(imageButton.IsEnabled);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void ImageButton_WithUnknownAction_IsDisabled()
    {
        var imageButton = new ImageButtonWidgetConfig
        {
            Id = "ib",
            Width = 120,
            Height = 120,
            Action = new UnknownActionConfig(
                "http",
                System.Text.Json.JsonDocument.Parse("""{ "type": "http", "url": "https://example.test" }""").RootElement.Clone()),
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(imageButton));

        Assert.False(imageButton.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.action.unknown");
    }

    // ---- User-requested addition: byte/rate display unit (entry-level containment) ----

    [Fact]
    public void Gauge_UnknownDisplayUnit_FallsBackToAutoWithAWarning()
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g",
            Width = 200,
            Height = 200,
            Sensor = SensorKeys.CpuTotal,
            DisplayUnit = DisplayUnit.Unknown,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(gauge));

        Assert.Equal(DisplayUnit.Auto, gauge.DisplayUnit);
        Assert.True(gauge.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.displayUnit");
    }

    [Fact]
    public void Bar_UnknownDisplayUnit_FallsBackToAutoWithAWarning()
    {
        var bar = new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Sensor = SensorKeys.CpuTotal,
            DisplayUnit = DisplayUnit.Unknown,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(bar));

        Assert.Equal(DisplayUnit.Auto, bar.DisplayUnit);
        Assert.True(bar.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.displayUnit");
    }

    [Fact]
    public void Sparkline_UnknownDisplayUnit_FallsBackToAutoWithAWarning()
    {
        var sparkline = new SparklineWidgetConfig
        {
            Id = "sp",
            Width = 240,
            Height = 100,
            Sensor = SensorKeys.NetworkDown,
            DisplayUnit = DisplayUnit.Unknown,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(sparkline));

        Assert.Equal(DisplayUnit.Auto, sparkline.DisplayUnit);
        Assert.True(sparkline.IsEnabled);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.widget.displayUnit");
    }

    [Fact]
    public void Gauge_ValidDisplayUnit_IsPreservedWithoutAWarning()
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g",
            Width = 200,
            Height = 200,
            Sensor = SensorKeys.NetworkDown,
            DisplayUnit = DisplayUnit.Megabytes,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(gauge));

        Assert.Equal(DisplayUnit.Megabytes, gauge.DisplayUnit);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.displayUnit");
    }

    // ---- Rate default scale (user-reported fix: a rate bar/gauge was always full) ----

    [Fact]
    public void Bar_RateSensor_WithTheUntouchedDefaultRange_AdoptsTheRateScaleSilently()
    {
        var bar = new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Sensor = SensorKeys.NetworkDown,
            Minimum = BarWidgetConfig.DefaultMinimum,
            Maximum = BarWidgetConfig.DefaultMaximum,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(bar));

        Assert.Equal(ConfigValidator.DefaultRateMinimum, bar.Minimum);
        Assert.Equal(ConfigValidator.DefaultRateMaximum, bar.Maximum);
        Assert.True(bar.IsEnabled);

        // Silent normalization: nothing for the user to act on, so no diagnostic at all.
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void Gauge_RateSensor_WithTheUntouchedDefaultRange_AdoptsTheRateScaleSilently()
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g",
            Width = 200,
            Height = 200,
            Sensor = SensorKeys.DiskRead,
            Minimum = GaugeWidgetConfig.DefaultMinimum,
            Maximum = GaugeWidgetConfig.DefaultMaximum,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(gauge));

        Assert.Equal(ConfigValidator.DefaultRateMinimum, gauge.Minimum);
        Assert.Equal(ConfigValidator.DefaultRateMaximum, gauge.Maximum);
        Assert.True(gauge.IsEnabled);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(SensorKeys.CpuTotal)]
    [InlineData(SensorKeys.RamUsed)]
    [InlineData(SensorKeys.CpuTemperature)]
    public void Bar_NonRateSensor_WithTheDefaultRange_KeepsThePercentScale(string sensor)
    {
        var bar = new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Sensor = sensor,
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(bar));

        Assert.Equal(BarWidgetConfig.DefaultMinimum, bar.Minimum);
        Assert.Equal(BarWidgetConfig.DefaultMaximum, bar.Maximum);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity is ConfigDiagnosticSeverity.Warning or ConfigDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(SensorKeys.CpuTotal)]
    [InlineData(SensorKeys.CpuTemperature)]
    public void Gauge_NonRateSensor_WithTheDefaultRange_KeepsThePercentScale(string sensor)
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g",
            Width = 200,
            Height = 200,
            Sensor = sensor,
        };

        ConfigValidator.Validate(CreateConfig(gauge));

        Assert.Equal(GaugeWidgetConfig.DefaultMinimum, gauge.Minimum);
        Assert.Equal(GaugeWidgetConfig.DefaultMaximum, gauge.Maximum);
    }

    [Fact]
    public void Bar_RateSensor_WithAnExplicitRange_IsNeverRescaled()
    {
        var bar = new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Sensor = SensorKeys.NetworkDown,
            Minimum = 0,
            Maximum = 1000,
        };

        ConfigValidator.Validate(CreateConfig(bar));

        Assert.Equal(0, bar.Minimum);
        Assert.Equal(1000, bar.Maximum);
    }

    [Fact]
    public void Gauge_RateSensor_WithAnExplicitRange_IsNeverRescaled()
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g",
            Width = 200,
            Height = 200,
            Sensor = SensorKeys.NetworkUp,
            Minimum = 0,
            Maximum = 50,
        };

        ConfigValidator.Validate(CreateConfig(gauge));

        Assert.Equal(0, gauge.Minimum);
        Assert.Equal(50, gauge.Maximum);
    }

    [Fact]
    public void Bar_UnknownSensor_WithTheDefaultRange_IsNotRescaled()
    {
        var bar = new BarWidgetConfig
        {
            Id = "bar",
            Width = 240,
            Height = 64,
            Sensor = "custom.sensor",
        };

        ConfigValidator.Validate(CreateConfig(bar));

        Assert.False(bar.IsEnabled);
        Assert.Equal(BarWidgetConfig.DefaultMinimum, bar.Minimum);
        Assert.Equal(BarWidgetConfig.DefaultMaximum, bar.Maximum);
    }

    [Fact]
    public void Bar_MissingSensor_WithTheDefaultRange_IsNotRescaled()
    {
        var bar = new BarWidgetConfig { Id = "bar", Width = 240, Height = 64, Sensor = null };

        ConfigValidator.Validate(CreateConfig(bar));

        Assert.False(bar.IsEnabled);
        Assert.Equal(BarWidgetConfig.DefaultMinimum, bar.Minimum);
        Assert.Equal(BarWidgetConfig.DefaultMaximum, bar.Maximum);
    }

    [Fact]
    public void Gauge_UnknownSensor_WithTheDefaultRange_IsNotRescaled()
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g",
            Width = 200,
            Height = 200,
            Sensor = "custom.sensor",
        };

        ConfigValidator.Validate(CreateConfig(gauge));

        Assert.Equal(GaugeWidgetConfig.DefaultMinimum, gauge.Minimum);
        Assert.Equal(GaugeWidgetConfig.DefaultMaximum, gauge.Maximum);
    }

    [Fact]
    public void DefaultRangeConstants_MatchTheBarAndGaugeDefaults()
    {
        // The rate-scale rule keys off this exact pair, so the three declarations cannot drift.
        Assert.Equal(BarWidgetConfig.DefaultMinimum, ConfigValidator.DefaultRangeMinimum);
        Assert.Equal(BarWidgetConfig.DefaultMaximum, ConfigValidator.DefaultRangeMaximum);
        Assert.Equal(GaugeWidgetConfig.DefaultMinimum, ConfigValidator.DefaultRangeMinimum);
        Assert.Equal(GaugeWidgetConfig.DefaultMaximum, ConfigValidator.DefaultRangeMaximum);
        Assert.Equal(0, ConfigValidator.DefaultRateMinimum);
        Assert.Equal(12_500_000d, ConfigValidator.DefaultRateMaximum);
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
