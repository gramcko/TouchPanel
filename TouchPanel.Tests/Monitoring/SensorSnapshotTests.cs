using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Stable sensor-key mapping on the snapshot (T-11, RF-034/RF-063): documented keys resolve to the
/// matching field and unit, unknown/malformed keys fail, and unavailable sensors are represented
/// explicitly instead of being fabricated as zero.
/// </summary>
public sealed class SensorSnapshotTests
{
    private const double GiB = 1024d * 1024d * 1024d;
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static SensorSnapshot CreatePopulatedSnapshot() => new()
    {
        CapturedAt = CapturedAt,
        CpuTotalPercent = 42,
        CpuCorePercents = [10, 20, 30],
        GpuUsagePercent = 60,
        VramUsedBytes = 2 * GiB,
        VramTotalBytes = 8 * GiB,
        RamUsedBytes = 8 * GiB,
        RamTotalBytes = 16 * GiB,
        RamPercent = 50,
        DiskActivityPercent = 7,
        DiskReadBytesPerSecond = 1024,
        DiskWriteBytesPerSecond = 2048,
        NetworkUpBytesPerSecond = 100,
        NetworkDownBytesPerSecond = 200,
        CpuTemperatureCelsius = 55,
        GpuTemperatureCelsius = 61,
        FanSpeedsRpm = [1200, 900],
    };

    [Theory]
    [InlineData(SensorKeys.CpuTotal, 42d, SensorUnit.Percent)]
    [InlineData(SensorKeys.GpuUsage, 60d, SensorUnit.Percent)]
    [InlineData(SensorKeys.GpuVramUsed, 2 * GiB, SensorUnit.Bytes)]
    [InlineData(SensorKeys.GpuVramTotal, 8 * GiB, SensorUnit.Bytes)]
    [InlineData(SensorKeys.GpuVramPercent, 25d, SensorUnit.Percent)]
    [InlineData(SensorKeys.RamUsed, 8 * GiB, SensorUnit.Bytes)]
    [InlineData(SensorKeys.RamTotal, 16 * GiB, SensorUnit.Bytes)]
    [InlineData(SensorKeys.RamPercent, 50d, SensorUnit.Percent)]
    [InlineData(SensorKeys.DiskActivity, 7d, SensorUnit.Percent)]
    [InlineData(SensorKeys.DiskRead, 1024d, SensorUnit.BytesPerSecond)]
    [InlineData(SensorKeys.DiskWrite, 2048d, SensorUnit.BytesPerSecond)]
    [InlineData(SensorKeys.NetworkUp, 100d, SensorUnit.BytesPerSecond)]
    [InlineData(SensorKeys.NetworkDown, 200d, SensorUnit.BytesPerSecond)]
    [InlineData(SensorKeys.CpuTemperature, 55d, SensorUnit.Celsius)]
    [InlineData(SensorKeys.GpuTemperature, 61d, SensorUnit.Celsius)]
    public void TryGetReading_MapsDocumentedKeysToTheirFieldAndUnit(string key, double expected, SensorUnit unit)
    {
        var snapshot = CreatePopulatedSnapshot();

        Assert.True(snapshot.TryGetReading(key, out var reading));
        Assert.True(reading.IsAvailable);
        Assert.Equal(expected, reading.Value);
        Assert.Equal(unit, reading.Unit);
    }

    [Theory]
    [InlineData("cpu.core.0", 10d)]
    [InlineData("cpu.core.1", 20d)]
    [InlineData("cpu.core.2", 30d)]
    [InlineData("fan.0", 1200d)]
    [InlineData("fan.1", 900d)]
    public void TryGetReading_MapsIndexedKeys(string key, double expected)
    {
        var snapshot = CreatePopulatedSnapshot();

        Assert.True(snapshot.TryGetReading(key, out var reading));
        Assert.True(reading.IsAvailable);
        Assert.Equal(expected, reading.Value);
    }

    [Fact]
    public void TryGetReading_IsCaseInsensitiveAndTrimsWhitespace()
    {
        var snapshot = CreatePopulatedSnapshot();

        Assert.True(snapshot.TryGetReading(" CPU.Total ", out var reading));
        Assert.Equal(42, reading.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cpu")]
    [InlineData("cpu.total.extra")]
    [InlineData("cpu.core")]
    [InlineData("cpu.core.")]
    [InlineData("cpu.core.-1")]
    [InlineData("cpu.core.1.5")]
    [InlineData("cpu.core.abc")]
    [InlineData("fan")]
    [InlineData("fan.")]
    [InlineData("fan.x")]
    [InlineData("sparkline")]
    public void TryGetReading_UnknownOrMalformedKeys_ReturnFalse(string key)
    {
        var snapshot = CreatePopulatedSnapshot();

        Assert.False(snapshot.TryGetReading(key, out _));
    }

    [Fact]
    public void TryGetReading_KnownFamilyWithOutOfRangeIndex_IsUnavailableNotUnknown()
    {
        var snapshot = CreatePopulatedSnapshot();

        Assert.True(snapshot.TryGetReading("cpu.core.99", out var core));
        Assert.False(core.IsAvailable);

        Assert.True(snapshot.TryGetReading("fan.99", out var fan));
        Assert.False(fan.IsAvailable);
    }

    [Fact]
    public void CreateUnavailable_HasNoFabricatedZeros()
    {
        var snapshot = SensorSnapshot.CreateUnavailable(CapturedAt);

        Assert.Equal(CapturedAt, snapshot.CapturedAt);
        Assert.Null(snapshot.CpuTotalPercent);
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
        Assert.True(snapshot.CpuCorePercents.IsEmpty);
        Assert.True(snapshot.FanSpeedsRpm.IsEmpty);

        // Known keys still resolve, but explicitly as unavailable values.
        Assert.True(snapshot.TryGetReading(SensorKeys.CpuTotal, out var reading));
        Assert.False(reading.IsAvailable);
    }
}
