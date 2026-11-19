using Serilog.Events;
using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Sparkline view model (T-07, RF-041/M2-RF-411..416): maps the read-only bounded history onto
/// points with gaps, resolves auto/manual scale, stays unavailable when there is no available
/// sample, updates only when the history/scale changed, marshals background snapshot signals to the
/// UI thread, requests tracking through the history seam, disables on unknown sensor keys, degrades
/// on a read failure, and never renders more points than the store capacity allows. All fakes are
/// deterministic and touch no hardware.
/// </summary>
public sealed class SparklineWidgetTests
{
    [Fact]
    public void Constructor_ReadsExistingHistory_AndAutoScalesOverFiniteSamples()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 10, 20, 30);

        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history);

        Assert.Equal(new double?[] { 10, 20, 30 }, viewModel.Points);
        Assert.Equal(10, viewModel.ScaleMinimum);
        Assert.Equal(30, viewModel.ScaleMaximum);
        Assert.True(viewModel.IsAvailable);
        Assert.Equal(SensorKeys.CpuTotal, viewModel.SensorKey);
    }

    [Fact]
    public void HistoryWithGaps_PreservesNulls_AndAutoScalesOverFiniteSamplesOnly()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 10, null, 40, null, 20);

        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history);

        Assert.Equal(new double?[] { 10, null, 40, null, 20 }, viewModel.Points);
        Assert.Equal(10, viewModel.ScaleMinimum);
        Assert.Equal(40, viewModel.ScaleMaximum);
        Assert.True(viewModel.IsAvailable);
    }

    [Fact]
    public void AllUnavailable_IsUnavailable_WithTheNeutralRange_AndNoFabricatedZero()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, null, null);

        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history);

        Assert.False(viewModel.IsAvailable);
        Assert.Equal(new double?[] { null, null }, viewModel.Points);
        Assert.Equal(0, viewModel.ScaleMinimum);
        Assert.Equal(100, viewModel.ScaleMaximum);
    }

    [Fact]
    public void ExplicitScale_IsUsedRegardlessOfTheData()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 10, 20);

        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(minimum: 0, maximum: 100),
            history);

        Assert.Equal(0, viewModel.ScaleMinimum);
        Assert.Equal(100, viewModel.ScaleMaximum);
    }

    [Fact]
    public void InvalidExplicitScale_FallsBackToAutoScale()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 10, 20);

        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(minimum: 50, maximum: 10),
            history);

        Assert.Equal(10, viewModel.ScaleMinimum);
        Assert.Equal(20, viewModel.ScaleMaximum);
    }

    [Fact]
    public void FlatAutoScale_ExpandsAroundTheValue_SoTheLineIsVisible()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 5, 5);

        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history);

        Assert.True(viewModel.ScaleMinimum < 5);
        Assert.True(viewModel.ScaleMaximum > 5);
        Assert.Equal(5, (viewModel.ScaleMinimum + viewModel.ScaleMaximum) / 2d);
    }

    [Fact]
    public void SnapshotAvailable_ReReadsTheHistory()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 1);
        var source = new FakeSnapshotSource();
        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history, source);

        history.Set(SensorKeys.CpuTotal, 1, 2, 3);
        source.Publish(Cpu(3));

        Assert.Equal(new double?[] { 1, 2, 3 }, viewModel.Points);
    }

    [Fact]
    public void NetworkDown_FromTheRealHistoryStore_MovesAsSnapshotsAreRecorded()
    {
        // End to end: the store records net.down from each snapshot and the sparkline reads the
        // moving history back through the public boundary (no hardware involved).
        using var history = new SensorHistoryStore(TimeSpan.FromSeconds(1));
        history.Track(SensorKeys.NetworkDown);
        var source = new FakeSnapshotSource();
        var viewModel = new SparklineWidgetViewModel(CreateConfig(sensor: SensorKeys.NetworkDown), history, source);

        history.Record(new SensorSnapshot { NetworkDownBytesPerSecond = 0 });
        source.Publish(new SensorSnapshot { NetworkDownBytesPerSecond = 0 });
        Assert.Equal(new double?[] { 0 }, viewModel.Points);
        Assert.True(viewModel.IsAvailable);

        history.Record(new SensorSnapshot { NetworkDownBytesPerSecond = 1048576 });
        source.Publish(new SensorSnapshot { NetworkDownBytesPerSecond = 1048576 });
        Assert.Equal(new double?[] { 0, 1048576 }, viewModel.Points);
    }

    [Fact]
    public void UnchangedHistory_RaisesNoNotifications()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 1, 2);
        var source = new FakeSnapshotSource();
        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history, source);

        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        // A second snapshot with the same retained history must not re-render (M2-RF-413).
        source.Publish(Cpu(2));
        Assert.Empty(changes);

        history.Set(SensorKeys.CpuTotal, 1, 2, 4);
        source.Publish(Cpu(4));
        Assert.Contains(nameof(SparklineWidgetViewModel.Points), changes);
    }

    [Fact]
    public void SnapshotUpdates_AreMarshalledThroughTheDispatcher()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 1);
        var source = new FakeSnapshotSource();
        var dispatched = new List<Action>();
        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(),
            history,
            source,
            dispatch: dispatched.Add);

        history.Set(SensorKeys.CpuTotal, 1, 2);
        source.Publish(Cpu(2));

        // The background signal must not mutate UI state directly.
        Assert.Equal(new double?[] { 1 }, viewModel.Points);
        var update = Assert.Single(dispatched);

        update();
        Assert.Equal(new double?[] { 1, 2 }, viewModel.Points);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTheSnapshotSource()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 1);
        var source = new FakeSnapshotSource();
        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history, source);

        viewModel.Dispose();

        history.Set(SensorKeys.CpuTotal, 1, 9);
        source.Publish(Cpu(9));

        Assert.Equal(new double?[] { 1 }, viewModel.Points);
    }

    [Fact]
    public void RequestsTrackingForItsMetric_ThroughTheHistorySeam()
    {
        var history = new FakeSensorHistory();
        var requested = new List<string>();

        _ = new SparklineWidgetViewModel(
            CreateConfig(),
            history,
            requestTracking: requested.Add);

        Assert.Equal([SensorKeys.CpuTotal], requested);
    }

    [Fact]
    public void UnknownSensorKey_DisablesTheWidget_AndDoesNotRequestTracking()
    {
        var history = new FakeSensorHistory();
        var requested = new List<string>();
        var (logger, sink) = TestLoggerFactory.Create();

        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(sensor: "not.a.sensor"),
            history,
            requestTracking: requested.Add,
            logger: logger);

        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.ShowsDisabledIndicator);
        Assert.Contains("not.a.sensor", viewModel.DisabledReason!, StringComparison.Ordinal);
        Assert.Empty(requested);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void DisabledConfig_FromValidation_IsPreserved()
    {
        var config = CreateConfig();
        config.IsEnabled = false;
        config.DisabledReason = "disabled by validation";

        var viewModel = new SparklineWidgetViewModel(config, new FakeSensorHistory());

        Assert.False(viewModel.IsEnabled);
        Assert.Equal("disabled by validation", viewModel.DisabledReason);
    }

    [Fact]
    public void NoHistorySource_StaysUnavailable()
    {
        var viewModel = new SparklineWidgetViewModel(CreateConfig());

        Assert.False(viewModel.IsAvailable);
        Assert.Empty(viewModel.Points);
    }

    [Fact]
    public void ReadFailure_DegradesToUnavailable_AndLogsOnceThenRecovers()
    {
        var history = new FakeSensorHistory { GetHistoryException = new InvalidOperationException("bad data") };
        history.Set(SensorKeys.CpuTotal, 1, 2);
        var (logger, sink) = TestLoggerFactory.Create();

        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history, logger: logger);

        Assert.False(viewModel.IsAvailable);
        Assert.Empty(viewModel.Points);
        Assert.Single(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);

        // A later successful read degrades gracefully rather than latching unavailable.
        history.GetHistoryException = null;
        viewModel.Refresh();

        Assert.True(viewModel.IsAvailable);
        Assert.Equal(new double?[] { 1, 2 }, viewModel.Points);
    }

    [Fact]
    public void RealStore_BoundsRenderedPointsToTheRetainedCapacity()
    {
        // Window 10 s at 1 s interval → capacity 10 (M2-RF-415).
        using var store = new SensorHistoryStore(TimeSpan.FromSeconds(1), windowSeconds: 10);
        store.Track(SensorKeys.CpuTotal);
        for (var i = 1; i <= 15; i++)
        {
            store.Record(new SensorSnapshot { CpuTotalPercent = i });
        }

        var viewModel = new SparklineWidgetViewModel(CreateConfig(), store);

        Assert.Equal(10, viewModel.Points.Count);
        Assert.Equal(6, viewModel.Points[0]);
        Assert.Equal(15, viewModel.Points[^1]);
        Assert.True(viewModel.Points.Count <= store.Capacity);
    }

    [Fact]
    public void RealStore_Window300_OnOneSecondInterval_RendersAtMost300Points()
    {
        // windowSeconds 300 at a 1 s interval → capacity ceil(300/1) = 300 (M2-RF-691/M2-RF-415).
        using var store = new SensorHistoryStore(TimeSpan.FromSeconds(1));
        store.Reconfigure(windowSeconds: 300, interval: TimeSpan.FromSeconds(1));
        store.Track(SensorKeys.CpuTotal);
        for (var i = 1; i <= 400; i++)
        {
            store.Record(new SensorSnapshot { CpuTotalPercent = i });
        }

        Assert.Equal(300, store.Capacity);

        var viewModel = new SparklineWidgetViewModel(CreateConfig(windowSeconds: 300), store);

        Assert.Equal(300, viewModel.Points.Count);
        Assert.Equal(101, viewModel.Points[0]);
        Assert.Equal(400, viewModel.Points[^1]);
    }

    [Fact]
    public void TwoSparklines_WithDifferentWindows_EachRenderTheirOwnWindowSlice()
    {
        // One bounded store sized to the maximum window (300); each sparkline slices its own window.
        using var store = new SensorHistoryStore(TimeSpan.FromSeconds(1));
        store.Reconfigure(windowSeconds: 300, interval: TimeSpan.FromSeconds(1));
        store.Track(SensorKeys.CpuTotal);
        for (var i = 1; i <= 300; i++)
        {
            store.Record(new SensorSnapshot { CpuTotalPercent = i });
        }

        var shortWindow = new SparklineWidgetViewModel(CreateConfig(windowSeconds: 60), store);
        var longWindow = new SparklineWidgetViewModel(CreateConfig(windowSeconds: 300), store);

        Assert.Equal(60, shortWindow.Points.Count);
        Assert.Equal(241, shortWindow.Points[0]);
        Assert.Equal(300, shortWindow.Points[^1]);

        Assert.Equal(300, longWindow.Points.Count);
        Assert.Equal(1, longWindow.Points[0]);
        Assert.Equal(300, longWindow.Points[^1]);

        // Both stay bounded by the shared store capacity.
        Assert.True(shortWindow.Points.Count <= store.Capacity);
        Assert.True(longWindow.Points.Count <= store.Capacity);
    }

    [Fact]
    public void WindowSlice_PreservesGaps()
    {
        using var store = new SensorHistoryStore(TimeSpan.FromSeconds(1));
        store.Reconfigure(windowSeconds: 300, interval: TimeSpan.FromSeconds(1));
        store.Track(SensorKeys.CpuTotal);
        for (var i = 1; i <= 100; i++)
        {
            store.Record(new SensorSnapshot { CpuTotalPercent = i == 85 ? (double?)null : i });
        }

        var viewModel = new SparklineWidgetViewModel(CreateConfig(windowSeconds: 60), store);

        Assert.Equal(60, viewModel.Points.Count);
        Assert.Equal(41, viewModel.Points[0]);
        Assert.Null(viewModel.Points[44]); // sample 85 is a gap
        Assert.Equal(100, viewModel.Points[^1]);
    }

    [Fact]
    public void FilledStyle_SetsIsFilled()
    {
        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(style: SparklineStyle.Filled),
            new FakeSensorHistory());

        Assert.True(viewModel.IsFilled);
    }

    // ---- User-requested addition: optional current-value readout (no schema bump) ----

    [Fact]
    public void ShowValue_DefaultsFalse_AndReflectsTheConfigurationAndAppliesLive()
    {
        var viewModel = new SparklineWidgetViewModel(CreateConfig(), new FakeSensorHistory());

        Assert.False(viewModel.ShowValue);

        viewModel.ApplyConfig(CreateConfig(showValue: true));
        Assert.True(viewModel.ShowValue);

        viewModel.ApplyConfig(CreateConfig(showValue: false));
        Assert.False(viewModel.ShowValue);
    }

    [Theory]
    [InlineData(DisplayUnit.Auto, "1 MB/s")]
    [InlineData(DisplayUnit.AutoKilobytes, "1 MB/s")]
    [InlineData(DisplayUnit.Bytes, "1048576 B/s")]
    [InlineData(DisplayUnit.Kilobytes, "1024 KB/s")]
    [InlineData(DisplayUnit.Megabytes, "1 MB/s")]
    [InlineData(DisplayUnit.Gigabytes, "0 GB/s")]
    public void ValueText_FormatsTheLatestRateSamplePerDisplayUnit(DisplayUnit displayUnit, string expected)
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.NetworkDown, 0, 1048576);

        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: displayUnit),
            history);

        Assert.Equal(expected, viewModel.ValueText);
    }

    [Fact]
    public void ValueText_AutoKilobytes_ShowsKBBelowTheThresholdAndMBAboveIt()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.NetworkDown, 524288);

        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: DisplayUnit.AutoKilobytes),
            history);

        Assert.Equal("512 KB/s", viewModel.ValueText);

        history.Set(SensorKeys.NetworkDown, 1572864);
        viewModel.ApplyConfig(CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: DisplayUnit.AutoKilobytes));

        Assert.Equal("1.5 MB/s", viewModel.ValueText);
    }

    [Fact]
    public void ValueText_Auto_UsesTheHumanFriendlyByteUnit()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.RamUsed, 1572864);

        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(sensor: SensorKeys.RamUsed, displayUnit: DisplayUnit.Auto),
            history);

        Assert.Equal("1.5 MB", viewModel.ValueText);
    }

    [Fact]
    public void ValueText_UnknownDisplayUnit_FallsBackToAuto()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.RamUsed, 1572864);

        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(sensor: SensorKeys.RamUsed, displayUnit: DisplayUnit.Unknown),
            history);

        Assert.Equal("1.5 MB", viewModel.ValueText);
    }

    [Fact]
    public void ValueText_IsUnavailable_WhenTheNewestSampleIsMissing()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 10, null);

        var viewModel = new SparklineWidgetViewModel(CreateConfig(), history);

        Assert.True(viewModel.IsAvailable);
        Assert.Equal(SparklineWidgetViewModel.UnavailableText, viewModel.ValueText);
    }

    [Fact]
    public void ValueText_IsUnavailable_WithoutAnyHistory()
    {
        var viewModel = new SparklineWidgetViewModel(CreateConfig());

        Assert.Equal(SparklineWidgetViewModel.UnavailableText, viewModel.ValueText);
    }

    [Fact]
    public void ValueText_UpdatesWhenTheLatestSampleChanges_AndIsReformattedOnApplyConfig()
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.NetworkDown, 0);
        var source = new FakeSnapshotSource();
        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: DisplayUnit.Bytes),
            history,
            source);

        Assert.Equal("0 B/s", viewModel.ValueText);

        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        history.Set(SensorKeys.NetworkDown, 0, 1048576);
        source.Publish(new SensorSnapshot { NetworkDownBytesPerSecond = 1048576 });

        Assert.Equal("1048576 B/s", viewModel.ValueText);
        Assert.Contains(nameof(SparklineWidgetViewModel.ValueText), changes);

        // ApplyConfig re-formats the live readout without a rebuild.
        viewModel.ApplyConfig(
            CreateConfig(sensor: SensorKeys.NetworkDown, displayUnit: DisplayUnit.Megabytes));
        Assert.Equal("1 MB/s", viewModel.ValueText);
    }

    [Theory]
    [InlineData(5, SparklineWidgetConfig.MinWindowSeconds)]
    [InlineData(999999, SparklineWidgetConfig.MaxWindowSeconds)]
    [InlineData(double.NaN, SparklineWidgetConfig.DefaultWindowSeconds)]
    public void WindowSeconds_IsClampedToTheConfiguredRange(double configured, double expected)
    {
        var viewModel = new SparklineWidgetViewModel(
            CreateConfig(windowSeconds: configured),
            new FakeSensorHistory());

        Assert.Equal(expected, viewModel.WindowSeconds);
    }

    private static SparklineWidgetConfig CreateConfig(
        string? sensor = SensorKeys.CpuTotal,
        double windowSeconds = SparklineWidgetConfig.DefaultWindowSeconds,
        SparklineStyle style = SparklineStyle.Line,
        double? minimum = null,
        double? maximum = null,
        bool showValue = false,
        DisplayUnit displayUnit = DisplayUnit.Auto) => new()
        {
            Id = "spark-1",
            X = 0,
            Y = 0,
            Width = 240,
            Height = 100,
            Label = "CPU trend",
            Sensor = sensor,
            WindowSeconds = windowSeconds,
            Style = style,
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
}
