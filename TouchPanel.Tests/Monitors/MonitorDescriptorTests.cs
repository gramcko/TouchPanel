using TouchPanel.Services.Monitors;

namespace TouchPanel.Tests.Monitors;

/// <summary>
/// Pure <see cref="MonitorDescriptor"/> identity composition: the canonical hardware ids the user
/// copies into configuration (D-17) and the display name used by logs and notifications.
/// </summary>
public sealed class MonitorDescriptorTests
{
    [Fact]
    public void HardwareId_CombinesManufacturerAndProductCodeInUpperCase()
    {
        var monitor = new MonitorDescriptor { Manufacturer = "del", ProductCode = "a0bc" };

        Assert.Equal("DEL-A0BC", monitor.HardwareId);
    }

    [Fact]
    public void HardwareId_MissingEitherPart_IsNull()
    {
        Assert.Null(new MonitorDescriptor { Manufacturer = "DEL" }.HardwareId);
        Assert.Null(new MonitorDescriptor { ProductCode = "A0BC" }.HardwareId);
        Assert.Null(new MonitorDescriptor().HardwareId);
    }

    [Fact]
    public void HardwareIdWithSerial_AppendsTrimmedSerial()
    {
        var monitor = new MonitorDescriptor { Manufacturer = "DEL", ProductCode = "A0BC", SerialNumber = " 4353 " };

        Assert.Equal("DEL-A0BC-4353", monitor.HardwareIdWithSerial);
    }

    [Fact]
    public void HardwareIdWithSerial_WithoutSerial_FallsBackToModelId()
    {
        var monitor = new MonitorDescriptor { Manufacturer = "DEL", ProductCode = "A0BC" };

        Assert.Equal("DEL-A0BC", monitor.HardwareIdWithSerial);
    }

    [Fact]
    public void HardwareIdWithSerial_WithoutModelId_IsNull()
    {
        var monitor = new MonitorDescriptor { SerialNumber = "4353" };

        Assert.Null(monitor.HardwareIdWithSerial);
    }

    [Fact]
    public void DisplayName_PrefersFriendlyNameThenHardwareIdThenGdiName()
    {
        Assert.Equal(
            "DELL U2415",
            new MonitorDescriptor
            {
                FriendlyName = "DELL U2415",
                Manufacturer = "DEL",
                ProductCode = "A0BC",
                SourceGdiName = @"\\.\DISPLAY1",
            }.DisplayName);

        Assert.Equal(
            "DEL-A0BC",
            new MonitorDescriptor
            {
                Manufacturer = "DEL",
                ProductCode = "A0BC",
                SourceGdiName = @"\\.\DISPLAY1",
            }.DisplayName);

        Assert.Equal(@"\\.\DISPLAY1", new MonitorDescriptor { SourceGdiName = @"\\.\DISPLAY1" }.DisplayName);
        Assert.Equal("unknown monitor", new MonitorDescriptor().DisplayName);
    }

    [Fact]
    public void PixelRect_ReportsExtentAndEmptyState()
    {
        var rect = new PixelRect(10, 20, 1920, 1080);

        Assert.False(rect.IsEmpty);
        Assert.Equal(1930, rect.Right);
        Assert.Equal(1100, rect.Bottom);

        Assert.True(PixelRect.Empty.IsEmpty);
        Assert.True(new PixelRect(0, 0, 0, 100).IsEmpty);
        Assert.True(new PixelRect(0, 0, 100, -1).IsEmpty);
    }
}
