using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Bar widget view model (RF-046/M3-RF-466/467): sensor resolution over the public snapshot
/// boundary, value→fraction mapping with clamping, the explicit "Unavailable" state (never a
/// fabricated zero), unknown-sensor disablement, update-on-change-only behavior, and disposal.
/// </summary>
public sealed class BarWidgetTests
{
    [Fact]
    public void AppliesTheConfiguredSensorValueAndMapsProgress()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(), source);

        source.Publish(Cpu(42.5));

        Assert.True(bar.IsAvailable);
        Assert.Equal(42.5, bar.Value);
        Assert.Equal("42.5 %", bar.ValueText);
        Assert.Equal(42.5, bar.Progress);
    }

    [Fact]
    public void MapsAndClampsProgressToTheConfiguredRange()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(minimum: 0, maximum: 10), source);

        source.Publish(Cpu(7.5));
        Assert.Equal(75, bar.Progress);

        source.Publish(Cpu(25));
        Assert.Equal(100, bar.Progress);
        Assert.Equal(25, bar.Value);

        source.Publish(Cpu(-5));
        Assert.Equal(0, bar.Progress);
    }

    [Fact]
    public void InvalidRange_FallsBackToTheDefaultDisplayRange()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(minimum: 10, maximum: 5), source);

        Assert.Equal(BarWidgetConfig.DefaultMinimum, bar.Minimum);
        Assert.Equal(BarWidgetConfig.DefaultMaximum, bar.Maximum);
    }

    [Fact]
    public void UnavailableSnapshot_ShowsTheExplicitUnavailableState()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(), source);

        source.Publish(Cpu(null));

        Assert.False(bar.IsAvailable);
        Assert.Null(bar.Value);
        Assert.Equal(BarWidgetViewModel.UnavailableText, bar.ValueText);
        Assert.Equal(0, bar.Progress);
    }

    [Fact]
    public void UnknownSensorKey_DisablesTheWidgetWithAReason()
    {
        var source = new FakeSnapshotSource();
        var (logger, sink) = TestLoggerFactory.Create();
        var bar = new BarWidgetViewModel(CreateConfig(sensor: "not.a.sensor"), source, logger: logger);

        Assert.False(bar.IsEnabled);
        Assert.NotNull(bar.DisabledReason);
        Assert.True(bar.ShowsDisabledIndicator);
        Assert.Single(sink.Events);

        source.Publish(Cpu(10));
        Assert.False(bar.IsAvailable);
    }

    [Fact]
    public void MissingSensorConfiguration_StaysUnavailableWithoutLogging()
    {
        var source = new FakeSnapshotSource();
        var (logger, sink) = TestLoggerFactory.Create();
        var bar = new BarWidgetViewModel(CreateConfig(sensor: null), source, logger: logger);

        Assert.False(bar.IsAvailable);
        Assert.Equal(BarWidgetViewModel.UnavailableText, bar.ValueText);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void UnchangedValue_RaisesNoNotifications()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(), source);
        var changes = new List<string?>();
        bar.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        source.Publish(Cpu(42));
        Assert.NotEmpty(changes);

        changes.Clear();
        source.Publish(Cpu(42));
        Assert.Empty(changes);

        source.Publish(Cpu(43));
        Assert.Contains(nameof(BarWidgetViewModel.ValueText), changes);
    }

    [Fact]
    public void SnapshotUpdates_AreMarshalledThroughTheDispatcher()
    {
        var source = new FakeSnapshotSource();
        var dispatched = new List<Action>();
        var bar = new BarWidgetViewModel(
            CreateConfig(),
            source,
            dispatch: action => dispatched.Add(action));

        source.Publish(Cpu(50));

        Assert.False(bar.IsAvailable);
        var update = Assert.Single(dispatched);
        update();

        Assert.True(bar.IsAvailable);
        Assert.Equal("50 %", bar.ValueText);
    }

    [Fact]
    public void ShowValue_ReflectsTheConfigurationAndIsRefreshable()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(showValue: false), source);

        Assert.False(bar.ShowValue);

        bar.ApplyConfig(new BarWidgetConfig { Id = "bar-1", Sensor = SensorKeys.CpuTotal, ShowValue = true });
        Assert.True(bar.ShowValue);
    }

    [Fact]
    public void Construction_AppliesTheLatestSnapshot()
    {
        var source = new FakeSnapshotSource();
        source.Publish(Cpu(33));

        var bar = new BarWidgetViewModel(CreateConfig(), source);

        Assert.True(bar.IsAvailable);
        Assert.Equal("33 %", bar.ValueText);
    }

    [Fact]
    public void TemperatureSensor_RendersCelsius_NotPercent()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(sensor: SensorKeys.CpuTemperature), source);

        source.Publish(Temperature(45));

        Assert.True(bar.IsAvailable);
        Assert.Equal(45, bar.Value);
        Assert.Equal("45 °C", bar.ValueText);
    }

    [Fact]
    public void UnavailableTemperatureSensor_ShowsUnavailable_NotZeroOrPercent()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(sensor: SensorKeys.CpuTemperature), source);

        source.Publish(new SensorSnapshot { CapturedAt = DateTimeOffset.UnixEpoch });

        Assert.False(bar.IsAvailable);
        Assert.Null(bar.Value);
        Assert.Equal(BarWidgetViewModel.UnavailableText, bar.ValueText);
    }

    [Fact]
    public void NetworkDownSensor_MovesAsThroughputChanges()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(sensor: SensorKeys.NetworkDown), source);

        source.Publish(Network(0));
        Assert.True(bar.IsAvailable);
        Assert.Equal("0 B/s", bar.ValueText);

        source.Publish(Network(1048576));
        Assert.Equal(1048576, bar.Value);
        Assert.Equal("1 MB/s", bar.ValueText);
    }

    [Fact]
    public void RateSensor_WithTheRateDefaultScale_DoesNotPinTheBarAtFull()
    {
        // User-reported bug: net.down publishes bytes/s, so the untouched 0–100 range clamped every
        // real reading to 100%. The normalized 0–12,500,000 B/s (100 Mbps) scale keeps the fill
        // proportional across the useful range.
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(
            CreateConfig(sensor: SensorKeys.NetworkDown, minimum: 0, maximum: ConfigValidator.DefaultRateMaximum),
            source);

        source.Publish(Network(1024));
        Assert.Equal(1024d / 12_500_000d * 100d, bar.Progress, precision: 9);

        source.Publish(Network(500 * 1024));
        Assert.Equal(4.096, bar.Progress, precision: 6);

        source.Publish(Network(12_500_000));
        Assert.Equal(100, bar.Progress);

        source.Publish(Network(20_000_000));
        Assert.Equal(100, bar.Progress);
        Assert.Equal(20_000_000, bar.Value);
    }

    [Fact]
    public void RateSensor_ThroughTheValidator_ProducesAProportionalFill()
    {
        // The exact shape the user reported: a bar with net.down and no explicit range. Validation
        // normalizes the scale, so the live view model built from the validated config is not full.
        var barConfig = CreateConfig(sensor: SensorKeys.NetworkDown);
        var panel = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages = [new PageConfig { Id = "main", Widgets = [barConfig] }],
        };

        var diagnostics = ConfigValidator.Validate(panel);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.widget.bar.range");

        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(barConfig, source);

        Assert.Equal(ConfigValidator.DefaultRateMaximum, bar.Maximum);

        source.Publish(Network(500 * 1024));
        Assert.Equal(4.096, bar.Progress, precision: 6);
    }

    [Fact]
    public void ByteSensor_HonorsTheConfiguredDisplayUnitLive()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(sensor: SensorKeys.RamUsed, displayUnit: DisplayUnit.Megabytes), source);

        source.Publish(Ram(1572864));
        Assert.Equal("1.5 MB", bar.ValueText);

        bar.ApplyConfig(CreateConfig(sensor: SensorKeys.RamUsed, displayUnit: DisplayUnit.Auto));

        Assert.Equal("1.5 MB", bar.ValueText);

        bar.ApplyConfig(CreateConfig(sensor: SensorKeys.RamUsed, displayUnit: DisplayUnit.Bytes));

        Assert.Equal("1572864 B", bar.ValueText);
    }

    [Fact]
    public void RateSensor_AutoKilobytes_SwitchesKBAndMBLive()
    {
        // User-requested addition: the download bar's thousands-of-KB readout becomes KB below the
        // 1024 KB threshold and MB at or above it, following the rate up and down.
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(
            CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: DisplayUnit.AutoKilobytes),
            source);

        source.Publish(Network(524288));
        Assert.Equal("512 KB/s", bar.ValueText);

        source.Publish(Network(1048576));
        Assert.Equal("1 MB/s", bar.ValueText);

        source.Publish(Network(1572864));
        Assert.Equal("1.5 MB/s", bar.ValueText);

        source.Publish(Network(2048));
        Assert.Equal("2 KB/s", bar.ValueText);

        // Live re-format through ApplyConfig, without a rebuild.
        bar.ApplyConfig(CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: DisplayUnit.AutoKilobytes));
        Assert.Equal("2 KB/s", bar.ValueText);
    }

    private static SensorSnapshot Ram(double? value) => new()
    {
        CapturedAt = DateTimeOffset.UnixEpoch,
        RamUsedBytes = value,
    };

    [Fact]
    public void Dispose_UnsubscribesFromTheSnapshotSource()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(), source);
        bar.Dispose();

        source.Publish(Cpu(77));

        Assert.False(bar.IsAvailable);
        Assert.Equal(BarWidgetViewModel.UnavailableText, bar.ValueText);
    }

    [Fact]
    public void ApplyConfig_ReMapsProgressWithoutARebuild()
    {
        var source = new FakeSnapshotSource();
        var bar = new BarWidgetViewModel(CreateConfig(minimum: 0, maximum: 100), source);
        source.Publish(Cpu(50));
        Assert.Equal(50, bar.Progress);

        bar.ApplyConfig(CreateConfig(minimum: 0, maximum: 10));

        Assert.Equal(100, bar.Progress);
    }

    private static BarWidgetConfig CreateConfig(
        string? sensor = SensorKeys.CpuTotal,
        double minimum = 0,
        double maximum = 100,
        bool showValue = true,
        DisplayUnit displayUnit = DisplayUnit.Auto) => new()
        {
            Id = "bar-1",
            X = 0,
            Y = 0,
            Width = 240,
            Height = 64,
            Label = "CPU",
            Sensor = sensor,
            Minimum = minimum,
            Maximum = maximum,
            ShowValue = showValue,
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
