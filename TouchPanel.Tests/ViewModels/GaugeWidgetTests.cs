using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Gauge widget view model (T-11, RF-033/RF-034/RF-038/RF-065): maps the configured sensor onto
/// the display range with clamping, shows an explicit "Unavailable" state (never a fabricated
/// zero), marshals snapshot updates through the injected dispatcher, and raises no notifications
/// when the resolved value did not change.
/// </summary>
public sealed class GaugeWidgetTests
{
    private const double GiB = 1024d * 1024d * 1024d;

    [Fact]
    public void AppliesTheConfiguredSensorValue()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(), source);

        source.Publish(Cpu(42.5));

        Assert.True(gauge.IsAvailable);
        Assert.Equal(42.5, gauge.Value);
        Assert.Equal("42.5 %", gauge.ValueText);
        Assert.Equal(42.5, gauge.Progress);
    }

    [Fact]
    public void MapsAndClampsProgressToTheConfiguredRange()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(minimum: 0, maximum: 10), source);

        source.Publish(Cpu(7.5));
        Assert.Equal(75, gauge.Progress);

        source.Publish(Cpu(25));
        Assert.Equal(100, gauge.Progress);
        Assert.Equal(25, gauge.Value);

        source.Publish(Cpu(-5));
        Assert.Equal(0, gauge.Progress);
        Assert.Equal(-5, gauge.Value);
    }

    [Fact]
    public void InvalidRange_FallsBackToTheDefaultDisplayRange()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(minimum: 10, maximum: 5), source);

        Assert.Equal(0, gauge.Minimum);
        Assert.Equal(100, gauge.Maximum);

        source.Publish(Cpu(50));
        Assert.Equal(50, gauge.Progress);
    }

    [Fact]
    public void UnavailableSnapshot_ShowsTheExplicitUnavailableState()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(), source);

        source.Publish(Cpu(null));

        Assert.False(gauge.IsAvailable);
        Assert.Null(gauge.Value);
        Assert.Equal(GaugeWidgetViewModel.UnavailableText, gauge.ValueText);
        Assert.Equal(0, gauge.Progress);
    }

    [Fact]
    public void UnavailableSnapshot_AfterAValue_ResetsToUnavailable()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(), source);

        source.Publish(Cpu(30));
        Assert.True(gauge.IsAvailable);

        // The sensor disappeared (driver/sensor failure): the gauge must show unavailable again.
        source.Publish(Cpu(null));
        Assert.False(gauge.IsAvailable);
        Assert.Equal(GaugeWidgetViewModel.UnavailableText, gauge.ValueText);
    }

    [Fact]
    public void UnknownSensorKey_ShowsUnavailable_AndLogsOnce()
    {
        var source = new FakeSnapshotSource();
        var (logger, sink) = TestLoggerFactory.Create();
        var gauge = new GaugeWidgetViewModel(CreateConfig(sensor: "not.a.sensor"), source, logger: logger);

        source.Publish(Cpu(10));
        source.Publish(Cpu(11));

        Assert.False(gauge.IsAvailable);
        Assert.Equal(GaugeWidgetViewModel.UnavailableText, gauge.ValueText);
        Assert.Single(sink.Events);
    }

    [Fact]
    public void MissingSensorConfiguration_StaysUnavailableWithoutLogging()
    {
        var source = new FakeSnapshotSource();
        var (logger, sink) = TestLoggerFactory.Create();
        var gauge = new GaugeWidgetViewModel(CreateConfig(sensor: null), source, logger: logger);

        source.Publish(Cpu(10));

        Assert.False(gauge.IsAvailable);
        Assert.Equal(GaugeWidgetViewModel.UnavailableText, gauge.ValueText);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void NoSnapshotSource_StaysUnavailable()
    {
        var gauge = new GaugeWidgetViewModel(CreateConfig());

        Assert.False(gauge.IsAvailable);
        Assert.Equal(GaugeWidgetViewModel.UnavailableText, gauge.ValueText);
    }

    [Fact]
    public void UnchangedValue_RaisesNoNotifications()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(), source);
        var changes = new List<string?>();
        gauge.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        source.Publish(Cpu(42));
        Assert.NotEmpty(changes);

        changes.Clear();
        source.Publish(Cpu(42));
        Assert.Empty(changes);

        source.Publish(Cpu(43));
        Assert.Contains(nameof(GaugeWidgetViewModel.ValueText), changes);
    }

    [Fact]
    public void SnapshotUpdates_AreMarshalledThroughTheDispatcher()
    {
        var source = new FakeSnapshotSource();
        var dispatched = new List<Action>();
        var gauge = new GaugeWidgetViewModel(
            CreateConfig(),
            source,
            dispatch: action => dispatched.Add(action));

        source.Publish(Cpu(50));

        // The background publication must not mutate UI state directly.
        Assert.False(gauge.IsAvailable);
        var update = Assert.Single(dispatched);

        update();

        Assert.True(gauge.IsAvailable);
        Assert.Equal("50 %", gauge.ValueText);
    }

    [Fact]
    public void Construction_AppliesTheLatestSnapshot()
    {
        var source = new FakeSnapshotSource();
        source.Publish(Cpu(33));

        var gauge = new GaugeWidgetViewModel(CreateConfig(), source);

        Assert.True(gauge.IsAvailable);
        Assert.Equal("33 %", gauge.ValueText);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTheSnapshotSource()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(), source);
        gauge.Dispose();

        source.Publish(Cpu(77));

        Assert.False(gauge.IsAvailable);
        Assert.Equal(GaugeWidgetViewModel.UnavailableText, gauge.ValueText);
    }

    [Fact]
    public void TemperatureSensor_RendersCelsius_NotPercent()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(sensor: SensorKeys.CpuTemperature), source);

        source.Publish(Temperature(45));

        Assert.True(gauge.IsAvailable);
        Assert.Equal(45, gauge.Value);
        Assert.Equal("45 °C", gauge.ValueText);
    }

    [Fact]
    public void UnavailableTemperatureSensor_ShowsUnavailable_NotZeroOrPercent()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(sensor: SensorKeys.CpuTemperature), source);

        // A machine without the CPU temperature sensor (no elevation/driver) publishes a snapshot
        // whose temperature field is null, never a fabricated zero or a percentage.
        source.Publish(new SensorSnapshot { CapturedAt = DateTimeOffset.UnixEpoch });

        Assert.False(gauge.IsAvailable);
        Assert.Null(gauge.Value);
        Assert.Equal(GaugeWidgetViewModel.UnavailableText, gauge.ValueText);
    }

    [Fact]
    public void NetworkDownSensor_MovesAsThroughputChanges()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(sensor: SensorKeys.NetworkDown), source);

        source.Publish(Network(0));
        Assert.True(gauge.IsAvailable);
        Assert.Equal("0 B/s", gauge.ValueText);

        source.Publish(Network(1048576));
        Assert.Equal(1048576, gauge.Value);
        Assert.Equal("1 MB/s", gauge.ValueText);
    }

    [Fact]
    public void RateSensor_WithTheRateDefaultScale_DoesNotPinTheGaugeAtFull()
    {
        // Same defect class as the bar: a gauge bound to net.down over the untouched 0–100 default
        // read as 100% for any real traffic. The 0–12,500,000 B/s scale keeps the fill proportional.
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(
            CreateConfig(sensor: SensorKeys.NetworkDown, minimum: 0, maximum: ConfigValidator.DefaultRateMaximum),
            source);

        source.Publish(Network(1024));
        Assert.Equal(1024d / 12_500_000d * 100d, gauge.Progress, precision: 9);

        source.Publish(Network(500 * 1024));
        Assert.Equal(4.096, gauge.Progress, precision: 6);

        source.Publish(Network(12_500_000));
        Assert.Equal(100, gauge.Progress);

        source.Publish(Network(20_000_000));
        Assert.Equal(100, gauge.Progress);
    }

    [Fact]
    public void RateSensor_ThroughTheValidator_ProducesAProportionalGauge()
    {
        var gaugeConfig = CreateConfig(sensor: SensorKeys.NetworkDown);
        var panel = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages = [new PageConfig { Id = "main", Widgets = [gaugeConfig] }],
        };

        ConfigValidator.Validate(panel);

        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(gaugeConfig, source);

        Assert.Equal(ConfigValidator.DefaultRateMaximum, gauge.Maximum);

        source.Publish(Network(12_500_000));
        Assert.Equal(100, gauge.Progress);
    }

    [Theory]
    [InlineData(42.5, SensorUnit.Percent, "42.5 %")]
    [InlineData(55, SensorUnit.Celsius, "55 °C")]
    [InlineData(1200, SensorUnit.Rpm, "1200 RPM")]
    [InlineData(8 * GiB, SensorUnit.Bytes, "8 GB")]
    [InlineData(1048576, SensorUnit.BytesPerSecond, "1 MB/s")]
    [InlineData(512, SensorUnit.Bytes, "512 B")]
    public void FormatValue_UsesTheSensorUnit(double value, SensorUnit unit, string expected)
    {
        Assert.Equal(expected, GaugeWidgetViewModel.FormatValue(value, unit));
    }

    // ---- User-requested addition: per-widget byte/data-rate display unit (B/KB/MB/GB) ----

    [Theory]
    [InlineData(1536, DisplayUnit.Bytes, "1536 B")]
    [InlineData(2048, DisplayUnit.Kilobytes, "2 KB")]
    [InlineData(1572864, DisplayUnit.Megabytes, "1.5 MB")]
    [InlineData(2147483648, DisplayUnit.Gigabytes, "2 GB")]
    public void FormatValue_FixedByteUnit_FormatsInTheChosenUnit(double value, DisplayUnit displayUnit, string expected)
    {
        Assert.Equal(expected, GaugeWidgetViewModel.FormatValue(value, SensorUnit.Bytes, displayUnit));
    }

    [Theory]
    [InlineData(1536, DisplayUnit.Bytes, "1536 B/s")]
    [InlineData(2048, DisplayUnit.Kilobytes, "2 KB/s")]
    [InlineData(1572864, DisplayUnit.Megabytes, "1.5 MB/s")]
    [InlineData(2147483648, DisplayUnit.Gigabytes, "2 GB/s")]
    public void FormatValue_FixedRateUnit_KeepsThePerSecondSuffix(double value, DisplayUnit displayUnit, string expected)
    {
        Assert.Equal(expected, GaugeWidgetViewModel.FormatValue(value, SensorUnit.BytesPerSecond, displayUnit));
    }

    [Theory]
    [InlineData(SensorUnit.Percent, DisplayUnit.Megabytes, "42.5 %")]
    [InlineData(SensorUnit.Celsius, DisplayUnit.Gigabytes, "55 °C")]
    [InlineData(SensorUnit.Rpm, DisplayUnit.Bytes, "1200 RPM")]
    public void FormatValue_NonByteSensor_IgnoresTheDisplayUnit(SensorUnit unit, DisplayUnit displayUnit, string expected)
    {
        var value = unit switch
        {
            SensorUnit.Percent => 42.5,
            SensorUnit.Celsius => 55,
            _ => 1200,
        };

        Assert.Equal(expected, GaugeWidgetViewModel.FormatValue(value, unit, displayUnit));
    }

    [Fact]
    public void ByteSensor_HonorsTheConfiguredDisplayUnitLive()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(sensor: SensorKeys.RamUsed), source);

        source.Publish(Ram(1572864));
        Assert.Equal("1.5 MB", gauge.ValueText);

        gauge.ApplyConfig(CreateConfig(sensor: SensorKeys.RamUsed, displayUnit: DisplayUnit.Kilobytes));

        Assert.Equal("1536 KB", gauge.ValueText);

        gauge.ApplyConfig(CreateConfig(sensor: SensorKeys.RamUsed, displayUnit: DisplayUnit.Auto));

        Assert.Equal("1.5 MB", gauge.ValueText);
    }

    [Fact]
    public void RateSensor_HonorsTheConfiguredDisplayUnitLive()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(CreateConfig(sensor: SensorKeys.NetworkDown), source);

        source.Publish(Network(1572864));
        Assert.Equal("1.5 MB/s", gauge.ValueText);

        gauge.ApplyConfig(CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: DisplayUnit.Kilobytes));

        Assert.Equal("1536 KB/s", gauge.ValueText);
    }

    // ---- User-requested addition: automatic binary KB/MB display unit ----

    [Theory]
    [InlineData(0, "0 KB")]
    [InlineData(1024, "1 KB")]
    [InlineData(524288, "512 KB")]
    [InlineData(1047552, "1023 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(943718400, "900 MB")]
    public void FormatValue_AutoKilobytes_UsesKBBelowTheThresholdAndMBAtOrAboveIt(double bytes, string expected)
    {
        Assert.Equal(expected, GaugeWidgetViewModel.FormatValue(bytes, SensorUnit.Bytes, DisplayUnit.AutoKilobytes));
    }

    [Theory]
    [InlineData(0, "0 KB/s")]
    [InlineData(1024, "1 KB/s")]
    [InlineData(524288, "512 KB/s")]
    [InlineData(1047552, "1023 KB/s")]
    [InlineData(1048576, "1 MB/s")]
    [InlineData(1572864, "1.5 MB/s")]
    [InlineData(943718400, "900 MB/s")]
    public void FormatValue_AutoKilobytesRate_KeepsThePerSecondSuffix(double bytes, string expected)
    {
        Assert.Equal(expected, GaugeWidgetViewModel.FormatValue(bytes, SensorUnit.BytesPerSecond, DisplayUnit.AutoKilobytes));
    }

    [Fact]
    public void RateSensor_AutoKilobytes_SwitchesUpAndDownLive()
    {
        var source = new FakeSnapshotSource();
        var gauge = new GaugeWidgetViewModel(
            CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: DisplayUnit.AutoKilobytes),
            source);

        source.Publish(Network(524288));
        Assert.Equal("512 KB/s", gauge.ValueText);

        source.Publish(Network(1572864));
        Assert.Equal("1.5 MB/s", gauge.ValueText);

        // Dropping below 1024 KB switches straight back to KB.
        source.Publish(Network(1024));
        Assert.Equal("1 KB/s", gauge.ValueText);
    }

    private static SensorSnapshot Ram(double? value) => new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        RamUsedBytes = value,
    };

    private static GaugeWidgetConfig CreateConfig(
        string? sensor = SensorKeys.CpuTotal,
        double minimum = 0,
        double maximum = 100,
        DisplayUnit displayUnit = DisplayUnit.Auto) => new()
        {
            Id = "gauge-1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Label = "CPU",
            Sensor = sensor,
            Minimum = minimum,
            Maximum = maximum,
            DisplayUnit = displayUnit,
        };

    private static SensorSnapshot Cpu(double? value) => new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        CpuTotalPercent = value,
    };

    private static SensorSnapshot Temperature(double? value) => new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        CpuTemperatureCelsius = value,
    };

    private static SensorSnapshot Network(double? value) => new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        NetworkDownBytesPerSecond = value,
    };
}
