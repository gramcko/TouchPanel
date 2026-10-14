using System.Collections.Immutable;
using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Sensor-catalog behavior (M5-RF-510/M5-RF-512): the fixed aggregate keys are always offered (so
/// the picker is never empty), a snapshot contributes per-core CPU and fan keys, and a configured
/// key that is not in the catalog is preserved as an extra entry. No hardware and no WPF involved.
/// </summary>
public sealed class SensorCatalogTests
{
    [Fact]
    public void GetSensorOptions_WithoutASnapshot_ReturnsTheFixedAggregateFallback()
    {
        var options = new SensorCatalog(new FakeSnapshotSource()).GetSensorOptions();

        Assert.NotEmpty(options);
        Assert.Contains(options, option => option.Key == SensorKeys.CpuTotal);
        Assert.Contains(options, option => option.Key == SensorKeys.GpuUsage);
        Assert.Contains(options, option => option.Key == SensorKeys.GpuVramPercent);
        Assert.Contains(options, option => option.Key == SensorKeys.RamPercent);
        Assert.Contains(options, option => option.Key == SensorKeys.DiskActivity);
        Assert.Contains(options, option => option.Key == SensorKeys.NetworkUp);
        Assert.Contains(options, option => option.Key == SensorKeys.NetworkDown);
        Assert.Contains(options, option => option.Key == SensorKeys.CpuTemperature);
        Assert.Contains(options, option => option.Key == SensorKeys.GpuTemperature);
        Assert.DoesNotContain(options, option => option.Key.StartsWith(SensorKeys.CpuCorePrefix, StringComparison.Ordinal));
        Assert.DoesNotContain(options, option => option.Key.StartsWith(SensorKeys.FanPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void GetSensorOptions_WithNullSource_StillReturnsTheAggregateList()
    {
        Assert.NotEmpty(new SensorCatalog().GetSensorOptions());
    }

    [Theory]
    [InlineData(SensorKeys.CpuTotal, "%")]
    [InlineData(SensorKeys.RamPercent, "%")]
    [InlineData(SensorKeys.NetworkDown, "B/s")]
    [InlineData(SensorKeys.NetworkUp, "B/s")]
    [InlineData(SensorKeys.CpuTemperature, "°C")]
    [InlineData(SensorKeys.GpuTemperature, "°C")]
    public void GetSensorOptions_EveryOptionShowsItsUnitSoThePickerIsUnambiguous(string key, string unit)
    {
        var options = new SensorCatalog().GetSensorOptions();

        var option = Assert.Single(options, option => option.Key == key);
        Assert.Contains(unit, option.DisplayName, StringComparison.Ordinal);

        // The stable key is visible too, so a hand-edited panel.json value can be matched to a label.
        Assert.Contains(key, option.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSensorOptions_WithASnapshot_PerCoreAndFanOptionsShowTheirUnits()
    {
        var source = new FakeSnapshotSource();
        source.Publish(new SensorSnapshot
        {
            CapturedAt = DateTimeOffset.UnixEpoch,
            CpuCorePercents = ImmutableArray.Create<double?>(10),
            FanSpeedsRpm = ImmutableArray.Create(1200d),
        });

        var options = new SensorCatalog(source).GetSensorOptions();

        Assert.Contains("%", Assert.Single(options, o => o.Key == SensorKeys.CpuCore(0)).DisplayName, StringComparison.Ordinal);
        Assert.Contains("RPM", Assert.Single(options, o => o.Key == SensorKeys.Fan(0)).DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSensorOptions_WithASnapshot_IncludesPerCoreAndFanKeys()
    {
        var source = new FakeSnapshotSource();
        source.Publish(new SensorSnapshot
        {
            CapturedAt = DateTimeOffset.UnixEpoch,
            CpuCorePercents = ImmutableArray.Create<double?>(10, 20, null, 40),
            FanSpeedsRpm = ImmutableArray.Create(1200d, 900d),
        });

        var options = new SensorCatalog(source).GetSensorOptions();

        // Core positions are preserved (an unavailable middle core does not shift the indices).
        Assert.Contains(options, option => option.Key == SensorKeys.CpuCore(0));
        Assert.Contains(options, option => option.Key == SensorKeys.CpuCore(2));
        Assert.Contains(options, option => option.Key == SensorKeys.CpuCore(3));
        Assert.Contains(options, option => option.Key == SensorKeys.Fan(0));
        Assert.Contains(options, option => option.Key == SensorKeys.Fan(1));
        Assert.DoesNotContain(options, option => option.Key == SensorKeys.Fan(2));
    }

    [Fact]
    public void GetSensorOptions_WithAnUnknownCurrentKey_PreservesItAsAnExtraEntry()
    {
        var options = new SensorCatalog().GetSensorOptions("my.custom.sensor");

        var preserved = Assert.Single(options, option => option.Key == "my.custom.sensor");
        Assert.Contains("current", preserved.DisplayName, StringComparison.OrdinalIgnoreCase);
        // The aggregate fallback is still present.
        Assert.Contains(options, option => option.Key == SensorKeys.CpuTotal);
    }

    [Fact]
    public void GetSensorOptions_WithAKnownCurrentKey_DoesNotDuplicateIt()
    {
        var options = new SensorCatalog().GetSensorOptions(SensorKeys.CpuTotal);

        Assert.Single(options, option =>
            string.Equals(option.Key, SensorKeys.CpuTotal, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetSensorOptions_WithABlankCurrentKey_AddsNothingExtra()
    {
        var options = new SensorCatalog().GetSensorOptions("   ");
        Assert.All(options, option => Assert.False(string.IsNullOrWhiteSpace(option.Key)));
    }
}
