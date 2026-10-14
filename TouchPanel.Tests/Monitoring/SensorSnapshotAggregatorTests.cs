using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Snapshot aggregation (T-11, RF-063/RF-065): the pure projection from raw backend values must
/// mark NaN/infinite/invalid readings as unavailable (never zero), clamp percentages, preserve
/// per-core positions, and derive RAM/VRAM percentages from used/total.
/// </summary>
public sealed class SensorSnapshotAggregatorTests
{
    private const double GiB = 1024d * 1024d * 1024d;
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StampsTheCaptureInstant_AndKeepsValidValues()
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                CpuTotalPercent = 42.5,
                GpuUsagePercent = 31,
                CpuTemperatureCelsius = 55,
                FanSpeedsRpm = [1200, 900],
            },
            CapturedAt);

        Assert.Equal(CapturedAt, snapshot.CapturedAt);
        Assert.Equal(42.5, snapshot.CpuTotalPercent);
        Assert.Equal(31, snapshot.GpuUsagePercent);
        Assert.Equal(55, snapshot.CpuTemperatureCelsius);
        Assert.Equal([1200d, 900d], snapshot.FanSpeedsRpm.ToArray());
    }

    [Fact]
    public void Create_MapsNaNInfiniteAndNegativeReadingsToUnavailable()
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                CpuTotalPercent = double.NaN,
                GpuUsagePercent = double.PositiveInfinity,
                VramUsedBytes = -1,
                RamUsedBytes = double.NaN,
                RamTotalBytes = 16 * GiB,
                DiskReadBytesPerSecond = double.NegativeInfinity,
                NetworkUpBytesPerSecond = -5,
                CpuTemperatureCelsius = double.NaN,
            },
            CapturedAt);

        Assert.Null(snapshot.CpuTotalPercent);
        Assert.Null(snapshot.GpuUsagePercent);
        Assert.Null(snapshot.VramUsedBytes);
        Assert.Null(snapshot.RamUsedBytes);
        Assert.Null(snapshot.RamPercent);
        Assert.Null(snapshot.DiskReadBytesPerSecond);
        Assert.Null(snapshot.NetworkUpBytesPerSecond);
        Assert.Null(snapshot.CpuTemperatureCelsius);
        Assert.Equal(16 * GiB, snapshot.RamTotalBytes);
    }

    [Theory]
    [InlineData(150d, 100d)]
    [InlineData(-10d, null)]
    [InlineData(37.5, 37.5)]
    public void Create_ClampsOverBoundPercentagesAndMarksNegativeUnavailable(double input, double? expected)
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                CpuTotalPercent = input,
                DiskActivityPercent = input,
            },
            CapturedAt);

        Assert.Equal(expected, snapshot.CpuTotalPercent);
        Assert.Equal(expected, snapshot.DiskActivityPercent);
    }

    [Fact]
    public void Create_PreservesCorePositions_AndMarksInvalidCoresUnavailable()
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                CpuCorePercents = [10, double.NaN, 120, -1],
            },
            CapturedAt);

        Assert.Equal(4, snapshot.CpuCorePercents.Length);
        Assert.Equal(10, snapshot.CpuCorePercents[0]);
        Assert.Null(snapshot.CpuCorePercents[1]);
        Assert.Equal(100, snapshot.CpuCorePercents[2]);
        Assert.Null(snapshot.CpuCorePercents[3]);
    }

    [Fact]
    public void Create_ComputesRamPercentageFromUsedAndTotal()
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                RamUsedBytes = 8 * GiB,
                RamTotalBytes = 16 * GiB,
                RamPercent = 1, // ignored while used/total are valid
            },
            CapturedAt);

        Assert.Equal(50, snapshot.RamPercent);
    }

    [Fact]
    public void Create_UsesTheProvidedRamPercentage_WhenUsedOrTotalIsMissing()
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                RamPercent = 42,
            },
            CapturedAt);

        Assert.Equal(42, snapshot.RamPercent);
    }

    [Fact]
    public void Create_DoesNotComputeRamPercentage_WhenTotalIsZero()
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                RamUsedBytes = 1024,
                RamTotalBytes = 0,
            },
            CapturedAt);

        Assert.Null(snapshot.RamPercent);
    }

    [Fact]
    public void Create_ComputesVramPercentageFromUsedAndTotal()
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                VramUsedBytes = 2 * GiB,
                VramTotalBytes = 8 * GiB,
            },
            CapturedAt);

        Assert.Equal(25, snapshot.VramPercent);
    }

    [Fact]
    public void Create_DropsUnavailableFanReadings()
    {
        var snapshot = SensorSnapshotAggregator.Create(
            new SensorAggregationInput
            {
                FanSpeedsRpm = [double.NaN, 900, -3, 1500],
            },
            CapturedAt);

        Assert.Equal([900d, 1500d], snapshot.FanSpeedsRpm.ToArray());
    }

    [Fact]
    public void Create_EmptyInput_ProducesAnExplicitlyUnavailableSnapshot()
    {
        var snapshot = SensorSnapshotAggregator.Create(new SensorAggregationInput(), CapturedAt);

        Assert.Null(snapshot.CpuTotalPercent);
        Assert.True(snapshot.CpuCorePercents.IsEmpty);
        Assert.Null(snapshot.GpuUsagePercent);
        Assert.Null(snapshot.VramUsedBytes);
        Assert.Null(snapshot.VramTotalBytes);
        Assert.Null(snapshot.VramPercent);
        Assert.Null(snapshot.RamUsedBytes);
        Assert.Null(snapshot.RamTotalBytes);
        Assert.Null(snapshot.RamPercent);
        Assert.Null(snapshot.DiskActivityPercent);
        Assert.Null(snapshot.DiskReadBytesPerSecond);
        Assert.Null(snapshot.DiskWriteBytesPerSecond);
        Assert.Null(snapshot.NetworkUpBytesPerSecond);
        Assert.Null(snapshot.NetworkDownBytesPerSecond);
        Assert.Null(snapshot.CpuTemperatureCelsius);
        Assert.Null(snapshot.GpuTemperatureCelsius);
        Assert.True(snapshot.FanSpeedsRpm.IsEmpty);
    }

    [Fact]
    public void Create_DoesNotMutateTheInputCollections()
    {
        var cores = new List<double> { 10, 20 };
        var fans = new List<double> { 1000 };
        var input = new SensorAggregationInput { CpuCorePercents = cores, FanSpeedsRpm = fans };

        var snapshot = SensorSnapshotAggregator.Create(input, CapturedAt);
        cores.Add(30);
        fans.Add(2000);

        // ImmutableArray copies the values, so later mutation of the source list cannot leak in.
        Assert.Equal(2, snapshot.CpuCorePercents.Length);
        Assert.Single(snapshot.FanSpeedsRpm);
        Assert.Equal([10d, 20d], snapshot.CpuCorePercents.Select(value => value!.Value).ToArray());
    }
}
