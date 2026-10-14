using TouchPanel.Config;
using TouchPanel.Services.Monitors;

namespace TouchPanel.Tests.Monitors;

/// <summary>
/// RF-016/AC-06 matcher behavior, entirely hardware-free: priority chain (hardware identity,
/// device path/instance id, friendly name), missing EDID/device fields, duplicate-model tiebreaks
/// (primary, position, stable order), deterministic ordering, and no-match reason strings.
/// </summary>
public sealed class MonitorMatcherTests
{
    private const string DellModelId = "DEL-A0BC";
    private const string DellPathA = @"\\?\DISPLAY#DELA0BC#5&2f1a3b4c&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string DellPathB = @"\\?\DISPLAY#DELA0BC#5&1b2c3d4e&0&UID9999#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string DellFriendlyName = "DELL U2415";

    [Fact]
    public void Match_HardwareId_TakesPriorityOverDevicePathAndFriendlyName()
    {
        // Both monitors share the friendly name; the configured device path points at the other
        // unit. RF-016 says the highest-priority available criterion resolves the target.
        var dell = Monitor(manufacturer: "DEL", productCode: "A0BC", serial: "4353", devicePath: DellPathA, friendlyName: DellFriendlyName, gdiName: @"\\.\DISPLAY1");
        var acer = Monitor(manufacturer: "ACR", productCode: "1234", devicePath: DellPathB, friendlyName: DellFriendlyName, gdiName: @"\\.\DISPLAY2");

        var result = MonitorMatcher.Match(
            new MonitorIdentity { HardwareId = DellModelId, DevicePath = DellPathB, FriendlyName = DellFriendlyName },
            [acer, dell]);

        Assert.True(result.IsMatch);
        Assert.Same(dell, result.Monitor);
        Assert.Equal(MonitorMatchCriterion.HardwareId, result.Criterion);
        Assert.Contains(DellModelId, result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_UnitHardwareIdWithSerial_SelectsThePinnedUnitEvenWhenAnotherIsPrimary()
    {
        var primaryDell = Monitor(manufacturer: "DEL", productCode: "A0BC", serial: "4353", devicePath: DellPathA, friendlyName: DellFriendlyName, primary: true, gdiName: @"\\.\DISPLAY1");
        var secondaryDell = Monitor(manufacturer: "DEL", productCode: "A0BC", serial: "9999", devicePath: DellPathB, friendlyName: DellFriendlyName, gdiName: @"\\.\DISPLAY2");

        var result = MonitorMatcher.Match(
            new MonitorIdentity { HardwareId = "DEL-A0BC-9999" },
            [primaryDell, secondaryDell]);

        Assert.True(result.IsMatch);
        Assert.Same(secondaryDell, result.Monitor);
        Assert.Equal(MonitorMatchCriterion.HardwareId, result.Criterion);
    }

    [Fact]
    public void Match_ModelHardwareId_AmbiguousMatch_PrefersPrimaryMonitor()
    {
        var primaryDell = Monitor(manufacturer: "DEL", productCode: "A0BC", serial: "4353", friendlyName: DellFriendlyName, primary: true, gdiName: @"\\.\DISPLAY1");
        var secondaryDell = Monitor(manufacturer: "DEL", productCode: "A0BC", serial: "9999", friendlyName: DellFriendlyName, gdiName: @"\\.\DISPLAY2");

        var result = MonitorMatcher.Match(new MonitorIdentity { HardwareId = DellModelId }, [secondaryDell, primaryDell]);

        Assert.True(result.IsMatch);
        Assert.Same(primaryDell, result.Monitor);
        Assert.Contains("ambiguous match", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("primary-monitor preference", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_ModelHardwareId_AmbiguousWithoutPrimary_PrefersLeftMostPosition()
    {
        var right = Monitor(manufacturer: "DEL", productCode: "A0BC", serial: "9999", x: 1920, gdiName: @"\\.\DISPLAY2");
        var left = Monitor(manufacturer: "DEL", productCode: "A0BC", serial: "4353", x: 0, gdiName: @"\\.\DISPLAY1");

        var result = MonitorMatcher.Match(new MonitorIdentity { HardwareId = DellModelId }, [right, left]);

        Assert.True(result.IsMatch);
        Assert.Same(left, result.Monitor);
        Assert.Contains("left-most position", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_FriendlyNameDuplicates_SameX_PrefersTopMostPosition()
    {
        var bottom = Monitor(friendlyName: DellFriendlyName, x: 0, y: 1080, gdiName: @"\\.\DISPLAY2");
        var top = Monitor(friendlyName: DellFriendlyName, x: 0, y: 0, gdiName: @"\\.\DISPLAY1");

        var result = MonitorMatcher.Match(new MonitorIdentity { FriendlyName = DellFriendlyName }, [bottom, top]);

        Assert.True(result.IsMatch);
        Assert.Same(top, result.Monitor);
        Assert.Contains("top-most position", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_FullyIdenticalDuplicates_TiebreakUsesStableEnumerationOrder()
    {
        var first = Monitor(friendlyName: DellFriendlyName, gdiName: @"\\.\DISPLAY1");
        var second = Monitor(friendlyName: DellFriendlyName, gdiName: @"\\.\DISPLAY2");

        var result = MonitorMatcher.Match(new MonitorIdentity { FriendlyName = DellFriendlyName }, [first, second]);

        Assert.True(result.IsMatch);
        Assert.Same(first, result.Monitor);
        Assert.Contains("stable enumeration order", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_HardwareIdNotExposed_FallsBackToDevicePath()
    {
        var monitor = Monitor(devicePath: DellPathA, friendlyName: "Generic PnP Monitor", gdiName: @"\\.\DISPLAY1");

        var result = MonitorMatcher.Match(
            new MonitorIdentity { HardwareId = DellModelId, DevicePath = DellPathA },
            [monitor]);

        Assert.True(result.IsMatch);
        Assert.Same(monitor, result.Monitor);
        Assert.Equal(MonitorMatchCriterion.DevicePath, result.Criterion);
        Assert.Contains("hardwareId", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not exposed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_HardwareIdAndDevicePathNotExposed_FallsBackToFriendlyName()
    {
        var monitor = Monitor(friendlyName: "Touch Screen", gdiName: @"\\.\DISPLAY1");

        var result = MonitorMatcher.Match(
            new MonitorIdentity { HardwareId = DellModelId, DevicePath = DellPathA, FriendlyName = "Touch Screen" },
            [monitor]);

        Assert.True(result.IsMatch);
        Assert.Same(monitor, result.Monitor);
        Assert.Equal(MonitorMatchCriterion.FriendlyName, result.Criterion);
        Assert.Contains("hardwareId", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("devicePath", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_ConfiguredHardwareIdDiffersFromPresentIdentity_ReturnsNoMatch()
    {
        // The descriptor exposes EDID identity, so the criterion is applicable and must match;
        // falling through to the device path would risk placing the panel on a swapped monitor.
        var acer = Monitor(manufacturer: "ACR", productCode: "1234", devicePath: DellPathA, gdiName: @"\\.\DISPLAY1");

        var result = MonitorMatcher.Match(
            new MonitorIdentity { HardwareId = DellModelId, DevicePath = DellPathA },
            [acer]);

        Assert.False(result.IsMatch);
        Assert.Null(result.Monitor);
        Assert.Contains("hardwareId", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("matched none", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_DevicePath_MatchesMonitorDeviceInstanceId()
    {
        const string instanceId = @"MONITOR\DELA0BC\{4d36e96e-e325-11ce-bfc1-08002be10318}\0001";
        var monitor = Monitor(manufacturer: "DEL", productCode: "A0BC", monitorDeviceId: instanceId, gdiName: @"\\.\DISPLAY1");

        var result = MonitorMatcher.Match(new MonitorIdentity { DevicePath = instanceId }, [monitor]);

        Assert.True(result.IsMatch);
        Assert.Same(monitor, result.Monitor);
        Assert.Equal(MonitorMatchCriterion.DevicePath, result.Criterion);
    }

    [Fact]
    public void Match_UnitHardwareIdConfiguredButDescriptorHasNoSerial_ReturnsNoMatch()
    {
        var monitor = Monitor(manufacturer: "DEL", productCode: "A0BC", gdiName: @"\\.\DISPLAY1");

        var result = MonitorMatcher.Match(new MonitorIdentity { HardwareId = "DEL-A0BC-4353" }, [monitor]);

        Assert.False(result.IsMatch);
        Assert.Contains("matched none", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_NullIdentity_ReturnsNoMatchWithReason()
    {
        var result = MonitorMatcher.Match(null, [Monitor(manufacturer: "DEL", productCode: "A0BC")]);

        Assert.False(result.IsMatch);
        Assert.Null(result.Monitor);
        Assert.Equal(MonitorMatchCriterion.None, result.Criterion);
        Assert.Contains("no monitor identity criteria", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_EmptyOrWhitespaceIdentity_ReturnsNoMatchWithReason()
    {
        var identity = new MonitorIdentity { HardwareId = "  ", DevicePath = null, FriendlyName = "\t" };

        var result = MonitorMatcher.Match(identity, [Monitor(friendlyName: DellFriendlyName)]);

        Assert.False(result.IsMatch);
        Assert.Contains("no monitor identity criteria", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_NoMonitorsDetected_ReturnsNoMatchWithReason()
    {
        var result = MonitorMatcher.Match(new MonitorIdentity { HardwareId = DellModelId }, []);

        Assert.False(result.IsMatch);
        Assert.Contains("no monitors were detected", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_AllConfiguredFieldsUnavailable_ReturnsNoMatchListingSkippedFields()
    {
        var monitor = Monitor(friendlyName: "Generic PnP Monitor", gdiName: @"\\.\DISPLAY1");

        var result = MonitorMatcher.Match(
            new MonitorIdentity { HardwareId = DellModelId, DevicePath = DellPathA },
            [monitor]);

        Assert.False(result.IsMatch);
        Assert.Contains("hardwareId", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("devicePath", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_ComparisonsAreTrimmedAndCaseInsensitive()
    {
        var monitor = Monitor(manufacturer: "DEL", productCode: "A0BC", friendlyName: DellFriendlyName);

        var result = MonitorMatcher.Match(
            new MonitorIdentity { HardwareId = "  del-a0bc  ", FriendlyName = " dell u2415 " },
            [monitor]);

        Assert.True(result.IsMatch);
        Assert.Same(monitor, result.Monitor);
    }

    [Fact]
    public void Match_DevicePathNarrowsAmbiguousHardwareMatch()
    {
        var first = Monitor(manufacturer: "DEL", productCode: "A0BC", devicePath: DellPathA, gdiName: @"\\.\DISPLAY1");
        var second = Monitor(manufacturer: "DEL", productCode: "A0BC", devicePath: DellPathB, gdiName: @"\\.\DISPLAY2");

        var result = MonitorMatcher.Match(
            new MonitorIdentity { HardwareId = DellModelId, DevicePath = DellPathB },
            [first, second]);

        Assert.True(result.IsMatch);
        Assert.Same(second, result.Monitor);
        Assert.Equal(MonitorMatchCriterion.DevicePath, result.Criterion);
        Assert.Contains("hardwareId", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_IsDeterministicAcrossRepeatedCalls()
    {
        var identity = new MonitorIdentity { FriendlyName = DellFriendlyName };
        var monitors = new[]
        {
            Monitor(friendlyName: DellFriendlyName, x: 1920, gdiName: @"\\.\DISPLAY2"),
            Monitor(friendlyName: DellFriendlyName, x: 0, gdiName: @"\\.\DISPLAY1"),
        };

        var first = MonitorMatcher.Match(identity, monitors);
        var second = MonitorMatcher.Match(identity, monitors);

        Assert.Same(first.Monitor, second.Monitor);
        Assert.Equal(first.Reason, second.Reason);
    }

    private static MonitorDescriptor Monitor(
        string? manufacturer = null,
        string? productCode = null,
        string? serial = null,
        string? devicePath = null,
        string? monitorDeviceId = null,
        string? friendlyName = null,
        bool primary = false,
        int x = 0,
        int y = 0,
        int width = 1920,
        int height = 1080,
        string? gdiName = null) => new()
        {
            Manufacturer = manufacturer,
            ProductCode = productCode,
            SerialNumber = serial,
            DevicePath = devicePath,
            MonitorDeviceId = monitorDeviceId,
            FriendlyName = friendlyName,
            IsPrimary = primary,
            Bounds = new PixelRect(x, y, width, height),
            WorkArea = new PixelRect(x, y, width, height),
            SourceGdiName = gdiName,
        };
}
