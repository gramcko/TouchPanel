using System.Runtime.InteropServices;
using Serilog;
using TouchPanel.Interop.Monitors;
using TouchPanel.Tests.TestSupport;
using Xunit.Abstractions;

namespace TouchPanel.Tests.Monitors;

/// <summary>
/// Interop surface of <see cref="Win32MonitorEnumerator"/>: native struct sizes (layout drift would
/// silently corrupt the CCD buffers), pure EDID decoding, best-effort device-path parsing, a
/// real-API check of the CCD target/source structs, and an invariant check of real enumeration
/// (no throw, deterministic order, only usable descriptors). Matching logic itself is covered
/// hardware-free by <see cref="MonitorMatcherTests"/>.
/// </summary>
public sealed class Win32MonitorEnumeratorTests
{
    private readonly ITestOutputHelper _output;

    public Win32MonitorEnumeratorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NativeStructSizes_MatchWindowsSdkLayout()
    {
        Assert.Equal(8, Marshal.SizeOf<Win32MonitorNative.Luid>());
        Assert.Equal(20, Marshal.SizeOf<Win32MonitorNative.DisplayConfigDeviceInfoHeader>());
        Assert.Equal(20, Marshal.SizeOf<Win32MonitorNative.DisplayConfigPathSourceInfo>());
        Assert.Equal(48, Marshal.SizeOf<Win32MonitorNative.DisplayConfigPathTargetInfo>());
        Assert.Equal(72, Marshal.SizeOf<Win32MonitorNative.DisplayConfigPathInfo>());
        Assert.Equal(84, Marshal.SizeOf<Win32MonitorNative.DisplayConfigSourceDeviceName>());
        Assert.Equal(420, Marshal.SizeOf<Win32MonitorNative.DisplayConfigTargetDeviceName>());
        Assert.Equal(104, Marshal.SizeOf<Win32MonitorNative.MonitorInfoEx>());

        // DISPLAY_DEVICEW is 840 bytes on x64: cb + DeviceName[32] + DeviceString[128] +
        // StateFlags + DeviceID[128] + DeviceKey[128]. Missing StateFlags shifts DeviceID by
        // four bytes and silently yields garbage device ids.
        Assert.Equal(840, Marshal.SizeOf<Win32MonitorNative.DisplayDevice>());
        Assert.Equal(328, Marshal.OffsetOf<Win32MonitorNative.DisplayDevice>(nameof(Win32MonitorNative.DisplayDevice.DeviceID)));
    }

    [Theory]
    [InlineData(0x10AC, "DEL")]
    [InlineData(0x0443, "ABC")]
    [InlineData(0xAC10, "DEL")] // Byte-swapped form, accepted only when the direct decode fails.
    public void DecodeEdidManufacturer_ValidValue_DecodesThreeLetterCode(ushort value, string expected)
    {
        Assert.Equal(expected, Win32MonitorNative.DecodeEdidManufacturer(value));
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x7C00)]
    [InlineData(0xFFFF)]
    public void DecodeEdidManufacturer_InvalidValue_ReturnsNull(ushort value)
    {
        Assert.Null(Win32MonitorNative.DecodeEdidManufacturer(value));
    }

    [Theory]
    [InlineData(0xA0BC, "A0BC")]
    [InlineData(0x0ABC, "0ABC")]
    public void DecodeEdidProductCode_NonZeroValue_FormatsFourHexDigits(ushort value, string expected)
    {
        Assert.Equal(expected, Win32MonitorNative.DecodeEdidProductCode(value));
    }

    [Fact]
    public void DecodeEdidProductCode_Zero_ReturnsNull()
    {
        Assert.Null(Win32MonitorNative.DecodeEdidProductCode(0));
    }

    [Fact]
    public void TryParseDevicePathIdentity_InterfacePath_ExtractsModelAndSerial()
    {
        const string path = @"\\?\DISPLAY#DELA0BC#5&2f1a3b4c&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        var parsed = Win32MonitorNative.TryParseDevicePathIdentity(path, out var manufacturer, out var productCode, out var serial);

        Assert.True(parsed);
        Assert.Equal("DEL", manufacturer);
        Assert.Equal("A0BC", productCode);
        Assert.Equal("4353", serial);
    }

    [Fact]
    public void TryParseDevicePathIdentity_InterfacePathWithoutUid_ExtractsModelOnly()
    {
        const string path = @"\\?\DISPLAY#DELA0BC#5&2f1a3b4c&0&0000#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        var parsed = Win32MonitorNative.TryParseDevicePathIdentity(path, out var manufacturer, out var productCode, out var serial);

        Assert.True(parsed);
        Assert.Equal("DEL", manufacturer);
        Assert.Equal("A0BC", productCode);
        Assert.Null(serial);
    }

    [Fact]
    public void TryParseDevicePathIdentity_InstancePath_ExtractsModelWithoutSerial()
    {
        const string path = @"MONITOR\DELA0BC\{4d36e96e-e325-11ce-bfc1-08002be10318}\0001";

        var parsed = Win32MonitorNative.TryParseDevicePathIdentity(path, out var manufacturer, out var productCode, out var serial);

        Assert.True(parsed);
        Assert.Equal("DEL", manufacturer);
        Assert.Equal("A0BC", productCode);
        Assert.Null(serial);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-device-path")]
    [InlineData(@"\\?\DISPLAY#SHORT#5&1&0&0000#{guid}")]
    [InlineData(@"MONITOR\SHORT\{4d36e96e-e325-11ce-bfc1-08002be10318}\0001")]
    public void TryParseDevicePathIdentity_UnparseablePath_ReturnsFalse(string? path)
    {
        var parsed = Win32MonitorNative.TryParseDevicePathIdentity(path, out var manufacturer, out var productCode, out var serial);

        Assert.False(parsed);
        Assert.Null(manufacturer);
        Assert.Null(productCode);
        Assert.Null(serial);
    }

    [Fact]
    public void Enumerate_OnTheRealMachine_NeverThrowsAndReturnsDeterministicallyOrderedUsableDescriptors()
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var enumerator = new Win32MonitorEnumerator(logger);

        var monitors = enumerator.Enumerate();

        Assert.NotNull(monitors);

        // Diagnostic output so a failed native call that silently degrades to "no monitors" is
        // visible when inspecting test results on a real desktop.
        _output.WriteLine($"Enumerated {monitors.Count} monitor(s):");
        foreach (var monitor in monitors)
        {
            _output.WriteLine(
                $"  {monitor.SourceGdiName} | {monitor.FriendlyName} | HardwareId={monitor.HardwareId} | WithSerial={monitor.HardwareIdWithSerial} | Path={monitor.DevicePath} | DeviceId={monitor.MonitorDeviceId} | Primary={monitor.IsPrimary} | Bounds={monitor.Bounds}");
        }

        // Every reported descriptor must be usable for placement; unusable paths are skipped and
        // logged by the enumerator instead of being returned.
        Assert.All(monitors, monitor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(monitor.SourceGdiName));
            Assert.False(monitor.Bounds.IsEmpty);
        });

        var sorted = monitors
            .OrderBy(monitor => monitor.SourceGdiName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(monitor => monitor.DevicePath ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(sorted, monitors);
    }

    [Fact]
    public void QueryDisplayConfig_OnTheRealMachine_ResolvesTargetAndSourceNames()
    {
        // Strongest practical marshalling check: the real API validates the DISPLAYCONFIG header
        // sizes, and the returned strings prove the field offsets. Skipped on sessions without an
        // active display topology (headless CI), so the suite still runs without hardware.
        var status = Win32MonitorNative.GetDisplayConfigBufferSizes(
            Win32MonitorNative.QdcOnlyActivePaths,
            out var pathCount,
            out var modeCount);

        if (status != 0 || pathCount == 0)
        {
            return;
        }

        var pathStride = Marshal.SizeOf<Win32MonitorNative.DisplayConfigPathInfo>();
        var pathBuffer = Marshal.AllocHGlobal(checked((int)pathCount * pathStride));
        var modeBuffer = Marshal.AllocHGlobal(checked((int)modeCount * 128));
        try
        {
            status = Win32MonitorNative.QueryDisplayConfig(
                Win32MonitorNative.QdcOnlyActivePaths,
                ref pathCount,
                pathBuffer,
                ref modeCount,
                modeBuffer,
                nint.Zero);

            Assert.Equal(0, status);
            Assert.True(pathCount > 0);

            for (var index = 0; index < pathCount; index++)
            {
                var path = Marshal.PtrToStructure<Win32MonitorNative.DisplayConfigPathInfo>(nint.Add(pathBuffer, index * pathStride));

                var target = new Win32MonitorNative.DisplayConfigTargetDeviceName
                {
                    Header = new Win32MonitorNative.DisplayConfigDeviceInfoHeader
                    {
                        Type = Win32MonitorNative.DeviceInfoGetTargetName,
                        Size = (uint)Marshal.SizeOf<Win32MonitorNative.DisplayConfigTargetDeviceName>(),
                        AdapterId = path.Target.AdapterId,
                        Id = path.Target.Id,
                    },
                    MonitorFriendlyDeviceName = string.Empty,
                    MonitorDevicePath = string.Empty,
                };
                var targetStatus = Win32MonitorNative.DisplayConfigGetDeviceInfo(ref target);

                var source = new Win32MonitorNative.DisplayConfigSourceDeviceName
                {
                    Header = new Win32MonitorNative.DisplayConfigDeviceInfoHeader
                    {
                        Type = Win32MonitorNative.DeviceInfoGetSourceName,
                        Size = (uint)Marshal.SizeOf<Win32MonitorNative.DisplayConfigSourceDeviceName>(),
                        AdapterId = path.Source.AdapterId,
                        Id = path.Source.Id,
                    },
                    ViewGdiDeviceName = string.Empty,
                };
                var sourceStatus = Win32MonitorNative.DisplayConfigGetDeviceInfo(ref source);

                _output.WriteLine(
                    $"path[{index}] targetStatus={targetStatus} name='{target.MonitorFriendlyDeviceName}' path='{target.MonitorDevicePath}' edid={target.EdidManufactureId:X4}/{target.EdidProductCodeId:X4} | sourceStatus={sourceStatus} gdi='{source.ViewGdiDeviceName}'");

                Assert.Equal(0, targetStatus);
                Assert.Equal(0, sourceStatus);
                Assert.StartsWith(@"\\.\DISPLAY", source.ViewGdiDeviceName, StringComparison.Ordinal);
                Assert.Contains('#', target.MonitorDevicePath);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pathBuffer);
            Marshal.FreeHGlobal(modeBuffer);
        }
    }
}
