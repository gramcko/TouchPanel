using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Pure unit-conversion coverage for the LibreHardwareMonitor adapter mapping (RF-062/RF-063):
/// application-unit passthroughs (percent, °C, RPM, MHz, V, A, W), binary byte conversions,
/// throughput conversions, and unavailable-value passthrough. No hardware or library types are
/// involved.
/// </summary>
public sealed class SensorValueConverterTests
{
    [Fact]
    public void ToApplicationValue_NativeApplicationUnits_PassThroughUnchanged()
    {
        SensorValueUnit[] units =
        [
            SensorValueUnit.Percent,
            SensorValueUnit.Celsius,
            SensorValueUnit.Rpm,
            SensorValueUnit.Megahertz,
            SensorValueUnit.Volt,
            SensorValueUnit.Ampere,
            SensorValueUnit.Watt,
            SensorValueUnit.BytesPerSecond,
        ];

        foreach (var unit in units)
        {
            Assert.Equal(1234.5, SensorValueConverter.ToApplicationValue(1234.5, unit));
            Assert.Equal(-20, SensorValueConverter.ToApplicationValue(-20, unit));
        }
    }

    [Fact]
    public void ToApplicationValue_Gigabytes_ConvertsToBinaryBytes()
    {
        Assert.Equal(2d * 1024 * 1024 * 1024, SensorValueConverter.ToApplicationValue(2, SensorValueUnit.Gigabytes));
    }

    [Fact]
    public void ToApplicationValue_Megabytes_ConvertsToBinaryBytes()
    {
        Assert.Equal(1.5 * 1024 * 1024, SensorValueConverter.ToApplicationValue(1.5, SensorValueUnit.Megabytes));
    }

    [Fact]
    public void ToApplicationValue_Kilobytes_ConvertsToBinaryBytes()
    {
        Assert.Equal(2048d, SensorValueConverter.ToApplicationValue(2, SensorValueUnit.Kilobytes));
    }

    [Fact]
    public void ToApplicationValue_BytesPerSecond_PassesThroughUnchanged()
    {
        // LHM 0.9.6 network throughput is already in bytes/second; converting it as KB/s inflated it
        // by 1024 and made net.down/net.up meaningless.
        Assert.Equal(125000d, SensorValueConverter.ToApplicationValue(125000, SensorValueUnit.BytesPerSecond));
    }

    [Fact]
    public void ToApplicationValue_MegabytesPerSecond_ConvertsToBytesPerSecond()
    {
        Assert.Equal(512d * 1024 * 1024, SensorValueConverter.ToApplicationValue(512, SensorValueUnit.MegabytesPerSecond));
    }

    [Fact]
    public void ToApplicationValue_KilobytesPerSecond_ConvertsToBytesPerSecond()
    {
        Assert.Equal(1024d * 8, SensorValueConverter.ToApplicationValue(8, SensorValueUnit.KilobytesPerSecond));
    }

    [Fact]
    public void ToApplicationValue_MissingValue_StaysUnavailableForEveryUnit()
    {
        foreach (var unit in Enum.GetValues<SensorValueUnit>())
        {
            Assert.Null(SensorValueConverter.ToApplicationValue(null, unit));
        }
    }
}
